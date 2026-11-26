using System.Diagnostics;
using System.Threading.Channels;
using DXMirror.Core.Diagnostics;
using DXMirror.Core.Settings;

namespace DXMirror.Core.Receiver;

/// <summary>Everything the controller needs to know about the installation.</summary>
public sealed record ReceiverEnvironment(
    string ExecutablePath,
    string DataDirectory,
    IReadOnlyDictionary<string, string> ExtraEnvironment,
    Func<string, string>? PathForChildProcess = null);

/// <summary>Per-start parameters that do not come from the settings file.</summary>
public sealed record ReceiverStartRequest(
    AppSettings Settings,
    nint VideoWindowHandle,
    string? MdnsIPv4);

/// <summary>
/// Owns the UxPlay child process: start / stop / restart, translates its status events into
/// <see cref="ReceiverSnapshot"/>s, restarts it after crashes (with back-off) and never lets a
/// receiver failure propagate into the UI. All public methods are thread-safe.
/// </summary>
public sealed class ReceiverController : IAsyncDisposable
{
    private const string Src = "Receiver";
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ReadyFallback = TimeSpan.FromSeconds(4);

    private readonly IReceiverProcessFactory _factory;
    private readonly AppLog _log;
    private readonly ReceiverEnvironment _env;
    private readonly ReceiverSession _session;
    private readonly RestartPolicy _restart = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Channel<Action> _events = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _eventLoop;
    private readonly Queue<string> _lastErrors = new();

    private IReceiverProcess? _process;
    private ReceiverStartRequest? _lastRequest;
    private UxPlayCapabilities? _caps;
    private string? _capsForExe;
    private Stopwatch _uptime = new();
    private bool _stopRequested;
    private CancellationTokenSource? _restartCts;
    private int _generation;
    private int _handledExitGeneration = -1;
    private double _volume = 1.0;
    private bool _muted;

    public event Action<ReceiverSnapshot>? SnapshotChanged;
    public ReceiverSnapshot Snapshot => _session.Snapshot;
    public UxPlayCapabilities? Capabilities => _caps;
    public int? ProcessId => _process is { HasExited: false } p ? p.Id : null;

    public ReceiverController(IReceiverProcessFactory factory, AppLog log, ReceiverEnvironment env, TimeProvider? time = null)
    {
        _factory = factory;
        _log = log;
        _env = env;
        _session = new ReceiverSession(time);
        _eventLoop = Task.Run(EventLoopAsync);
    }

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Sets DXMirror's own playback volume (0..1). Sent to the receiver's control channel; the
    /// receiver multiplies it with the iPhone's AirPlay volume. Kept and re-sent on every start.
    /// </summary>
    public void SetVolume(double volume, bool muted)
    {
        _volume = double.IsFinite(volume) ? Math.Clamp(volume, 0.0, 1.0) : 1.0;
        _muted = muted;
        _process?.SendLine(VolumeCommand(_volume, _muted));
    }

    /// <summary>
    /// Starts an MP4 recording. <paramref name="pathPrefix"/> is the file path without extension; the
    /// receiver writes "&lt;prefix&gt;.N.&lt;codecs&gt;.mp4" segments (a new segment per resolution change).
    /// </summary>
    public bool StartRecording(string pathPrefix)
    {
        if (_process is not { HasExited: false } p) return false;
        var prefix = pathPrefix.Replace('\\', '/').Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        p.SendLine("record_start " + prefix);
        _log.Info(Src, "Recording requested.");
        return true;
    }

    public void StopRecording()
    {
        _process?.SendLine("record_stop");
        _log.Info(Src, "Recording stop requested.");
    }

    /// <summary>Slider position → linear gain with a perceptual (squared) curve.</summary>
    internal static string VolumeCommand(double volume, bool muted)
    {
        var gain = muted ? 0.0 : volume * volume;
        return "volume " + gain.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task StartAsync(ReceiverStartRequest request, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CancelPendingRestart();
            _stopRequested = false;
            _restart.Reset();
            await StopCoreAsync().ConfigureAwait(false);
            await StartCoreAsync(request).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopRequested = true;
            CancelPendingRestart();
            await StopCoreAsync().ConfigureAwait(false);
            Post(() => Publish(_session.SetState(ReceiverState.Stopped)));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Ends the current iPhone session. UxPlay has no "kick client" command, so the receiver is
    /// restarted; the iPhone sees the session end and can reconnect immediately.
    /// </summary>
    public async Task DisconnectClientAsync()
    {
        var req = _lastRequest;
        if (req is null) return;
        _log.Info(Src, "Disconnect requested by user – restarting receiver.");
        await StartAsync(req).ConfigureAwait(false);
    }

    private async Task StartCoreAsync(ReceiverStartRequest request)
    {
        _lastRequest = request;
        var gen = Interlocked.Increment(ref _generation);
        Post(() => Publish(_session.SetState(ReceiverState.Starting)));
        lock (_lastErrors) _lastErrors.Clear();

        var exe = request.Settings.CustomUxPlayPath ?? _env.ExecutablePath;
        if (!File.Exists(exe))
        {
            Fault(gen, "receiver_missing", $"Receiver executable not found: {exe}");
            return;
        }

        Directory.CreateDirectory(_env.DataDirectory);
        var envVars = new Dictionary<string, string>(_env.ExtraEnvironment, StringComparer.OrdinalIgnoreCase)
        {
            ["DXMIRROR_EVENTS"] = "1",
            ["DXMIRROR_CONTROL"] = "1",
        };
        if (request.VideoWindowHandle != 0 && request.Settings.EmbedVideo && !request.Settings.AudioOnlyMode)
            envVars["DXMIRROR_HWND"] = ((long)request.VideoWindowHandle).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(request.MdnsIPv4))
            envVars["DXMIRROR_MDNS_IPV4"] = request.MdnsIPv4;
        // UxPlay looks for ~/.uxplayrc etc. relative to HOME; keep it inside our data dir
        envVars["HOME"] = ChildPath(_env.DataDirectory);

        if (_caps is null || _capsForExe != exe)
        {
            _caps = await ReadCapabilitiesAsync(exe, envVars).ConfigureAwait(false);
            _capsForExe = exe;
            _log.Info(Src, $"UxPlay version {_caps.Version ?? "unknown"}, {_caps.Options.Count} options detected.");
        }

        var paths = new ReceiverPaths(
            KeyFile: ChildPath(Path.Combine(_env.DataDirectory, "uxplay-key.pem")),
            StartupFile: Path.Combine(_env.DataDirectory, "uxplay.rc"));
        var lines = UxPlayStartupFile.BuildOptionLines(request.Settings, paths, _caps);
        await File.WriteAllTextAsync(paths.StartupFile, UxPlayStartupFile.BuildFileContent(lines), new System.Text.UTF8Encoding(false)).ConfigureAwait(false);
        foreach (var l in lines) _log.Debug(Src, "option: -" + l);

        IReceiverProcess proc;
        try
        {
            proc = _factory.Start(new ReceiverLaunch(exe, ["-rc", ChildPath(paths.StartupFile)], envVars, Path.GetDirectoryName(exe)!));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Fault(gen, "receiver_start_failed", "Receiver could not be started: " + ex.Message);
            return;
        }

        _process = proc;
        _uptime = Stopwatch.StartNew();
        if (request.Settings.Volume is var v && double.IsFinite(v)) { _volume = Math.Clamp(v, 0, 1); _muted = request.Settings.Muted; }
        proc.SendLine(VolumeCommand(_volume, _muted));
        _log.Info(Src, $"Receiver started (pid {proc.Id}), device name \"{request.Settings.DeviceName}\", mDNS IPv4 {request.MdnsIPv4 ?? "auto"}.");

        proc.OutputLine += line => OnOutput(gen, line);
        proc.ErrorLine += line => OnError(gen, line);
        proc.Exited += () => Post(() => _ = OnExitedAsync(gen, proc));
        if (proc.HasExited) Post(() => _ = OnExitedAsync(gen, proc)); // exited before we subscribed

        // Unpatched / older binaries emit no status events: treat a process that is still alive after a
        // few seconds as "ready" so the UI does not hang in "starting".
        _ = Task.Delay(ReadyFallback).ContinueWith(_ => Post(() =>
        {
            if (gen == _generation && !proc.HasExited && _session.Snapshot.State == ReceiverState.Starting)
            {
                _log.Warn(Src, "No status events from receiver – limited status display (unpatched UxPlay?).");
                Publish(_session.SetState(ReceiverState.Ready));
            }
        }), TaskScheduler.Default);
    }

    private async Task<UxPlayCapabilities> ReadCapabilitiesAsync(string exe, IReadOnlyDictionary<string, string> env)
    {
        try
        {
            var help = await _factory.RunToCompletionAsync(
                new ReceiverLaunch(exe, ["-h"], env, Path.GetDirectoryName(exe)!), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(help)) return UxPlayCapabilities.Parse(help, patched: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _log.Warn(Src, "Could not query receiver options: " + ex.Message);
        }
        return UxPlayCapabilities.Assume174();
    }

    private void OnOutput(int gen, string line)
    {
        var ev = UxPlayEventParser.TryParse(line);
        if (ev is null)
        {
            _log.Write(LogLevel.Info, "UxPlay", line);
            return;
        }
        if (ev.Name != "stats") _log.Debug("UxPlay", LogRedactor.Redact(line));
        Post(() =>
        {
            if (gen != _generation) return;
            var before = _session.Snapshot;
            var after = _session.Apply(ev);
            LogTransition(before, after, ev);
            Publish(after);
        });
    }

    private void OnError(int gen, string line)
    {
        _log.Write(LogLevel.Warning, "UxPlay", line);
        lock (_lastErrors)
        {
            _lastErrors.Enqueue(LogRedactor.Redact(line));
            while (_lastErrors.Count > 20) _lastErrors.Dequeue();
        }
    }

    private void LogTransition(ReceiverSnapshot before, ReceiverSnapshot after, UxPlayEvent ev)
    {
        switch (ev.Name)
        {
            case "started": _log.Info(Src, "Receiver ready, AirPlay services advertised via mDNS."); break;
            case "client_request": _log.Info(Src, $"Incoming connection from \"{after.ClientName}\" ({after.ClientModel})."); break;
            case "client_denied": _log.Warn(Src, "Connection denied."); break;
            case "conn_reset": _log.Warn(Src, $"Connection reset (reason {ev.Get("reason")})."); break;
            case "conn_close" when after.State == ReceiverState.Ready: _log.Info(Src, "Client disconnected."); break;
            case "video_size" when before.VideoWidth != after.VideoWidth || before.VideoHeight != after.VideoHeight:
                _log.Info(Src, $"Video stream {after.VideoCodec} {after.VideoWidth}x{after.VideoHeight}."); break;
            case "audio_format": _log.Info(Src, $"Audio stream {after.AudioFormat}."); break;
            case "recording": _log.Info(Src, $"Recording state: {ev.Get("state")}."); break;
        }
    }

    private async Task OnExitedAsync(int gen, IReceiverProcess proc)
    {
        // runs on the event loop up to the first await
        if (gen != _generation || _handledExitGeneration == gen) return;
        _handledExitGeneration = gen;
        var uptime = _uptime.Elapsed;
        var code = proc.ExitCode;
        if (_stopRequested)
        {
            Publish(_session.SetState(ReceiverState.Stopped));
            return;
        }

        string detail;
        lock (_lastErrors) detail = string.Join(" | ", _lastErrors.TakeLast(3));
        var reason = ClassifyFailure(detail);
        _log.Error(Src, $"Receiver exited unexpectedly (code {code?.ToString() ?? "?"}, uptime {uptime:hh\\:mm\\:ss}). {detail}");

        var req = _lastRequest;
        if (req is null || !req.Settings.AutoReconnect || reason is "port_in_use" or "receiver_missing" or "invalid_option")
        {
            Publish(_session.SetState(ReceiverState.Faulted, reason));
            return;
        }

        var delay = _restart.OnUnexpectedExit(uptime);
        if (delay is null)
        {
            _log.Error(Src, "Receiver keeps failing – automatic restart stopped.");
            Publish(_session.SetState(ReceiverState.Faulted, reason));
            return;
        }

        Publish(_session.SetState(ReceiverState.Starting, reason));
        _log.Info(Src, $"Restarting receiver in {delay.Value.TotalSeconds:0} s (attempt {_restart.ConsecutiveFailures}).");
        var cts = new CancellationTokenSource();
        _restartCts = cts;
        try
        {
            await Task.Delay(delay.Value, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (cts.IsCancellationRequested || _stopRequested || gen != _generation) return;
            await StopCoreAsync().ConfigureAwait(false);
            await StartCoreAsync(req).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    internal static string ClassifyFailure(string stderr)
    {
        // a rejected option fails identically on every start: restarting is pointless
        if (stderr.Contains("unknown option", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("invalid \"-", StringComparison.Ordinal)) return "invalid_option";
        if (stderr.Contains("5353", StringComparison.Ordinal)) return "mdns_port_blocked";
        if (stderr.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Error initializing raop", StringComparison.Ordinal)) return "port_in_use";
        if (stderr.Contains("no element", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("plugin", StringComparison.OrdinalIgnoreCase)) return "gstreamer_plugin_missing";
        return "receiver_crashed";
    }

    private async Task StopCoreAsync()
    {
        var proc = _process;
        _process = null;
        if (proc is null) return;
        Interlocked.Increment(ref _generation); // ignore late events from the old process
        try
        {
            if (!proc.HasExited)
            {
                Post(() => Publish(_session.SetState(ReceiverState.Stopping)));
                proc.SendLine("quit");
                proc.CloseInput();
                if (!await proc.WaitForExitAsync(StopGrace).ConfigureAwait(false))
                {
                    _log.Warn(Src, "Receiver did not stop in time – terminating.");
                    proc.Kill();
                    await proc.WaitForExitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }
            _log.Info(Src, "Receiver stopped.");
        }
        finally
        {
            proc.Dispose();
        }
    }

    private void Fault(int gen, string reason, string message)
    {
        _log.Error(Src, message);
        Post(() => { if (gen == _generation) Publish(_session.SetState(ReceiverState.Faulted, reason)); });
    }

    private void CancelPendingRestart()
    {
        var cts = Interlocked.Exchange(ref _restartCts, null);
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private string ChildPath(string path) => _env.PathForChildProcess?.Invoke(path) ?? path;

    private void Post(Action a) => _events.Writer.TryWrite(a);

    private void Publish(ReceiverSnapshot s)
    {
        try { SnapshotChanged?.Invoke(s); }
        catch (Exception ex) { _log.Error(Src, "Snapshot listener failed", ex); }
    }

    private async Task EventLoopAsync()
    {
        await foreach (var action in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { action(); }
            catch (Exception ex) { _log.Error(Src, "Receiver event handling failed", ex); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _events.Writer.TryComplete();
        try { await _eventLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (TimeoutException) { }
        _lifecycle.Dispose();
    }
}
