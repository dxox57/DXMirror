using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DXMirror.App.Infrastructure;
using DXMirror.App.Platform;
using DXMirror.App.Services;
using DXMirror.Core.Diagnostics;
using DXMirror.Core.Network;
using DXMirror.Core.Receiver;

namespace DXMirror.App.ViewModels;

public enum StatusTone { Idle, Ok, Busy, Warning, Error }

/// <summary>State and commands for the dashboard, the diagnostics page and the tray.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _tick;
    private ReceiverSnapshot _snapshot = new();
    private ReceiverState _previousState = ReceiverState.Stopped;
    private bool _volumeApplyPending;
    private DispatcherTimer? _volumeSaveTimer;

    public SettingsViewModel SettingsPage { get; }
    public ObservableCollection<string> LogLines { get; } = [];
    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public event Action? FullscreenToggleRequested;
    public event Action<string, string>? NotificationRequested;
    public event Action? StateChanged;

    internal MainViewModel(AppHost host, Dispatcher dispatcher)
    {
        _host = host;
        _ui = dispatcher;
        SettingsPage = new SettingsViewModel(host, this);

        _volume = host.Settings.Volume * 100;
        _muted = host.Settings.Muted;

        StartCommand = new AsyncCommand(StartAsync, () => !IsRunning, OnCommandError);
        StopCommand = new AsyncCommand(() => _host.StopReceiverAsync(), () => IsRunning, OnCommandError);
        DisconnectCommand = new AsyncCommand(() => _host.DisconnectAsync(), () => HasClient, OnCommandError);
        FullscreenCommand = new RelayCommand(() => FullscreenToggleRequested?.Invoke(), () => ShowVideo);
        MuteCommand = new RelayCommand(() => Muted = !Muted);
        ExportDiagnosticsCommand = new AsyncCommand(ExportDiagnosticsAsync, onError: OnCommandError);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        FirewallSetupCommand = new AsyncCommand(SetupFirewallAsync, onError: OnCommandError);
        RefreshDiagnosticsCommand = new AsyncCommand(RefreshDiagnosticsAsync, onError: OnCommandError);
        ClearCacheCommand = new RelayCommand(() => _host.ClearCache());
        ClearLogViewCommand = new RelayCommand(() => LogLines.Clear());
        RecordCommand = new RelayCommand(ToggleRecording, () => IsRunning || IsRecordingRequested);

        _host.Receiver.SnapshotChanged += s => _ui.InvokeAsync(() => ApplySnapshot(s));
        _host.NetworkChanged += () => _ui.InvokeAsync(UpdateNetwork);
        _host.Log.EntryAdded += e => _ui.InvokeAsync(() => AddLogLine(e));
        foreach (var e in _host.Log.Recent()) AddLogLine(e);
        Loc.LanguageChanged += () => { ApplySnapshot(_snapshot); UpdateNetwork(); };

        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), _ui);
        _tick.Start();
        UpdateNetwork();
        ApplySnapshot(_host.Receiver.Snapshot);
    }

    // ---------------------------------------------------------------- commands
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand FirewallSetupCommand { get; }
    public ICommand RefreshDiagnosticsCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand ClearLogViewCommand { get; }
    public ICommand RecordCommand { get; }

    // ---------------------------------------------------------------- in-app info message (toast)
    private string _toastText = "";
    public string ToastText { get => _toastText; private set { if (Set(ref _toastText, value)) OnPropertyChanged(nameof(ShowToast)); } }
    public bool ShowToast => _toastText.Length > 0;
    private DispatcherTimer? _toastTimer;

    /// <summary>Shows a short message at the top of the window for 4 seconds.</summary>
    public void Toast(string text)
    {
        ToastText = text;
        _toastTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal, (_, _) =>
        {
            _toastTimer!.Stop();
            ToastText = "";
        }, _ui);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---------------------------------------------------------------- recording
    private string? _recordPrefix;
    private DateTimeOffset? _recordingSince;
    private RecordingState _lastRecording;

    /// <summary>True from pressing "start" until the receiver confirms the stop.</summary>
    public bool IsRecordingRequested => _recordPrefix is not null;
    public string RecordButtonText => Loc.Get(IsRecordingRequested ? "Action.RecordStop" : "Action.RecordStart");
    private string _recordingText = "";
    public string RecordingText { get => _recordingText; private set => Set(ref _recordingText, value); }
    public bool ShowRecordingText => RecordingText.Length > 0;

    public string RecordingFolder => _host.Settings.RecordingFolder ?? AppPaths.DefaultRecordingFolder;

    private void ToggleRecording()
    {
        if (IsRecordingRequested)
        {
            _host.Receiver.StopRecording();
            return;
        }
        try
        {
            Directory.CreateDirectory(RecordingFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(Loc.Format("Rec.FolderError", RecordingFolder, ex.Message), "DXMirror", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var prefix = Path.Combine(RecordingFolder, $"DXMirror_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
        if (!_host.Receiver.StartRecording(prefix)) return;
        _recordPrefix = prefix;
        _recordingSince = null;
        UpdateRecordingText();
        OnPropertyChanged(nameof(IsRecordingRequested));
        OnPropertyChanged(nameof(RecordButtonText));
    }

    private void OnRecordingStateChanged(RecordingState now)
    {
        if (now == RecordingState.Failed && _recordPrefix is not null)
        {
            _host.Log.Error("Recording", "Receiver could not start the recording (see UxPlay messages above).");
            _host.Receiver.StopRecording();
            _recordPrefix = null;
            _recordingSince = null;
            Toast(Loc.Get("Rec.Failed"));
            OnPropertyChanged(nameof(IsRecordingRequested));
            OnPropertyChanged(nameof(RecordButtonText));
            UpdateRecordingText();
            return;
        }
        if (now == RecordingState.Recording && _recordingSince is null) _recordingSince = DateTimeOffset.Now;
        if (now == RecordingState.Off && _recordPrefix is not null)
        {
            var prefix = _recordPrefix;
            _recordPrefix = null;
            _recordingSince = null;
            var saved = FinalizeRecording(prefix);
            if (saved.Count > 0)
            {
                _host.Log.Info("Recording", $"Recording saved ({saved.Count} file(s)): {string.Join(", ", saved.Select(Path.GetFileName))}");
                Toast(Loc.Format("Rec.Saved", Path.GetFileName(saved[0])));
            }
            else
            {
                _host.Log.Warn("Recording", $"No recording file found for {Path.GetFileName(prefix)}.*.mp4 in {Path.GetDirectoryName(prefix)}");
                Toast(Loc.Get("Rec.NothingSaved"));
            }
            OnPropertyChanged(nameof(IsRecordingRequested));
            OnPropertyChanged(nameof(RecordButtonText));
        }
        UpdateRecordingText();
    }

    /// <summary>Renames the receiver's "prefix.N.H264.AAC.mp4" segments to "prefix.mp4" / "prefix_2.mp4".</summary>
    private List<string> FinalizeRecording(string prefix)
    {
        var result = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(prefix)!;
            var name = Path.GetFileName(prefix);
            var parts = Directory.GetFiles(dir, name + ".*.mp4")
                .OrderBy(f => f.Length).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                var target = Path.Combine(dir, parts.Count == 1 || i == 0 ? name + ".mp4" : $"{name}_{i + 1}.mp4");
                if (new FileInfo(parts[i]).Length == 0) continue;
                if (!File.Exists(target)) File.Move(parts[i], target);
                result.Add(File.Exists(target) ? target : parts[i]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Log.Warn("Recording", "Could not rename recording files: " + ex.Message);
        }
        return result;
    }

    private void UpdateRecordingText()
    {
        if (_recordPrefix is null) { RecordingText = ""; }
        else
        {
            RecordingText = _snapshot.Recording switch
            {
                RecordingState.Recording => Loc.Format("Rec.Recording",
                    _recordingSince is { } t ? (DateTimeOffset.Now - t).ToString(@"m\:ss", CultureInfo.InvariantCulture) : "0:00"),
                RecordingState.Waiting => Loc.Get("Rec.Waiting"),
                RecordingState.Armed => Loc.Get("Rec.Armed"),
                _ => Loc.Get("Rec.Starting"),
            };
        }
        OnPropertyChanged(nameof(ShowRecordingText));
    }

    public void OpenRecordingFolder()
    {
        Directory.CreateDirectory(RecordingFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{RecordingFolder}\"") { UseShellExecute = true });
    }

    public Task StartAsync() => _host.StartReceiverAsync();
    public Task StopAsync() => _host.StopReceiverAsync();
    public Task DisconnectAsync() => _host.DisconnectAsync();

    private void OnCommandError(Exception ex)
    {
        _host.Log.Error("UI", "Action failed", ex);
        MessageBox.Show(ex.Message, "DXMirror", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---------------------------------------------------------------- receiver status
    private ReceiverState _state;
    public ReceiverState State { get => _state; private set => Set(ref _state, value); }
    public bool IsRunning => State is not (ReceiverState.Stopped or ReceiverState.Faulted);
    public bool HasClient => _snapshot.HasClient;
    public bool IsStreaming => State == ReceiverState.Streaming;
    public bool ShowVideo => IsStreaming && _snapshot.Kind == StreamKind.ScreenMirroring
                             && _host.Settings.EmbedVideo && !_host.Settings.AudioOnlyMode;
    public bool ShowIdlePanel => !ShowVideo;
    public bool ShowPin => !string.IsNullOrEmpty(PendingPin);

    private string _statusTitle = "";
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    private string _statusDetail = "";
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    private StatusTone _tone;
    public StatusTone Tone { get => _tone; private set => Set(ref _tone, value); }
    private string _idleMessage = "";
    public string IdleMessage { get => _idleMessage; private set => Set(ref _idleMessage, value); }

    public string DeviceName => _host.Settings.DeviceName;
    public string ServiceStatus => Loc.Get(IsRunning ? "Status.ServiceRunning" : "Status.ServiceStopped");
    public string ReceiverVersion => _snapshot.ReceiverVersion ?? _host.Receiver.Capabilities?.Version ?? "–";
    public string AppVersion => AppPaths.Version;

    /// <summary>Process id of the running receiver (for embedding its video window).</summary>
    public int? ReceiverProcessId => _host.Receiver.ProcessId;
    /// <summary>True when the receiver's video window should be shown inside DXMirror.</summary>
    public bool EmbedVideoEnabled => _host.Settings.EmbedVideo && !_host.Settings.AudioOnlyMode;

    public string ClientName => _snapshot.ClientName ?? "–";
    public string ClientModel => _snapshot.ClientModel ?? "–";
    private string _duration = "–";
    public string Duration { get => _duration; private set => Set(ref _duration, value); }
    public string ConnectionStatus => Loc.Get("State." + State);
    public string Resolution => _snapshot.VideoWidth is { } w && _snapshot.VideoHeight is { } h
        ? $"{w} × {h} ({Loc.Get(_snapshot.IsLandscape ? "Video.Landscape" : "Video.Portrait")})"
        : "–";
    public string Codec => _snapshot.VideoCodec?.ToUpperInvariant() ?? "–";
    public string Fps => IsStreaming && _snapshot.Kind == StreamKind.ScreenMirroring ? _snapshot.Fps.ToString("0", CultureInfo.CurrentCulture) : "–";
    public string Bitrate => IsStreaming && _snapshot.VideoKbps > 0
        ? (_snapshot.VideoKbps >= 1000 ? $"{_snapshot.VideoKbps / 1000.0:0.0} Mbit/s" : $"{_snapshot.VideoKbps} kbit/s")
        : "–";
    public string AudioStatus => _snapshot.AudioActive ? Loc.Format("Audio.Active", _snapshot.AudioFormat ?? "?") : Loc.Get("Audio.Inactive");
    public string StreamKindText => Loc.Get("Kind." + _snapshot.Kind);
    public string? PendingPin => _snapshot.PendingPin;
    public string Quality
    {
        get
        {
            if (!IsStreaming) return "–";
            if (_snapshot.MissedHeartbeats > 2) return Loc.Get("Quality.Poor");
            if (_snapshot.Kind != StreamKind.ScreenMirroring) return Loc.Get("Quality.Good");
            // iOS only sends frames when the screen content changes, so a low fps on a static screen is normal
            return _snapshot.VideoKbps > 0 || _snapshot.Fps > 0 ? Loc.Get("Quality.Good") : Loc.Get("Quality.Static");
        }
    }
    public int MissedHeartbeats => _snapshot.MissedHeartbeats;

    // ---------------------------------------------------------------- network
    private string _adapterText = "–";
    public string AdapterText { get => _adapterText; private set => Set(ref _adapterText, value); }
    private string _networkStatus = "–";
    public string NetworkStatus { get => _networkStatus; private set => Set(ref _networkStatus, value); }
    private string _networkHint = "";
    public string NetworkHint { get => _networkHint; private set => Set(ref _networkHint, value); }

    // ---------------------------------------------------------------- discovery health check
    private string _discoveryWarning = "";
    /// <summary>Why an iPhone may not find DXMirror (empty = no problem found).</summary>
    public string DiscoveryWarning { get => _discoveryWarning; private set { if (Set(ref _discoveryWarning, value)) OnPropertyChanged(nameof(ShowDiscoveryWarning)); } }
    public bool ShowDiscoveryWarning => _discoveryWarning.Length > 0;
    private bool _bonjourMissing;
    public bool BonjourMissing { get => _bonjourMissing; private set => Set(ref _bonjourMissing, value); }
    public ICommand BonjourDownloadCommand => new RelayCommand(() =>
        Process.Start(new ProcessStartInfo(NetworkConflicts.BonjourDownloadUrl) { UseShellExecute = true }));
    private int _secondsSinceCheck = 1000;
    private bool _checkRunning;

    private async Task RunDiscoveryCheckAsync()
    {
        if (_checkRunning) return;
        _checkRunning = true;
        try
        {
            var ip = _host.CurrentAdapter?.PrimaryIPv4;
            var r = await Task.Run(() => NetworkConflicts.Check(ip));
            BonjourMissing = !r.BonjourInstalled;
            string text;
            if (!r.BonjourInstalled) text = Loc.Get("Warn.BonjourMissing");
            else if (!r.BonjourRunning) text = Loc.Get("Warn.BonjourStopped");
            else if (!r.BonjourOnAdapter && r.Blockers.Count > 0) text = Loc.Format("Warn.Blocked", string.Join(", ", r.Blockers));
            else if (!r.BonjourOnAdapter) text = Loc.Format("Warn.BonjourNotOnAdapter", ip ?? "?");
            else if (r.OtherAirPlayReceivers.Count > 0) text = Loc.Format("Warn.OtherReceivers", string.Join(", ", r.OtherAirPlayReceivers));
            else text = "";
            if (text != DiscoveryWarning && text.Length > 0)
                _host.Log.Warn("Discovery", $"bonjour installed={r.BonjourInstalled} running={r.BonjourRunning} onAdapter={r.BonjourOnAdapter} " +
                                            $"blockers=[{string.Join(",", r.Blockers)}] receivers=[{string.Join(",", r.OtherAirPlayReceivers)}]");
            DiscoveryWarning = text;
        }
        catch (Exception ex)
        {
            _host.Log.Warn("Discovery", "Discovery check failed: " + ex.Message);
        }
        finally
        {
            _checkRunning = false;
        }
    }

    private void UpdateNetwork()
    {
        var a = _host.CurrentAdapter;
        AdapterText = a is null ? Loc.Get("Network.None") : $"{a.Name} · {a.PrimaryIPv4}";
        NetworkStatus = a is null ? Loc.Get("Network.Offline") : Loc.Get("Network.Kind." + a.Kind);
        NetworkHint = a is null
            ? Loc.Get("Network.HintNone")
            : a.Kind is AdapterKind.Vpn or AdapterKind.Virtual ? Loc.Get("Network.HintVirtual") : "";
        Adapters.Clear();
        foreach (var x in _host.Adapters) Adapters.Add(x);
        SettingsPage.RefreshAdapters();
    }

    // ---------------------------------------------------------------- audio
    private double _volume;
    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, Math.Clamp(value, 0, 100))) return;
            if (_muted && value > 0) Muted = false;
            QueueVolumeApply();
        }
    }

    private bool _muted;
    public bool Muted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            OnPropertyChanged(nameof(MuteIcon));
            QueueVolumeApply();
        }
    }
    public string MuteIcon => _muted || _volume < 1 ? "" : "";

    private void QueueVolumeApply()
    {
        _volumeApplyPending = true;
        OnPropertyChanged(nameof(MuteIcon));
        _volumeSaveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, async (_, _) =>
        {
            _volumeSaveTimer!.Stop();
            _host.SaveVolume(_volume / 100.0, _muted);
            await ApplyVolumeNowAsync();
        }, _ui);
        _volumeSaveTimer.Stop();
        _volumeSaveTimer.Start();
    }

    private async Task ApplyVolumeNowAsync()
    {
        var changed = await _host.ApplyVolumeAsync();
        // 0 sessions: receiver has not opened its audio stream yet – retry on the next tick
        _volumeApplyPending = changed == 0 && _snapshot.AudioActive;
    }

    // ---------------------------------------------------------------- snapshot handling
    private void ApplySnapshot(ReceiverSnapshot s)
    {
        var prev = _snapshot;
        _snapshot = s;
        State = s.State;
        (StatusTitle, StatusDetail, Tone) = DescribeState(s);
        IdleMessage = s.State switch
        {
            ReceiverState.Ready => Loc.Get("Idle.Ready"),
            ReceiverState.Connecting => s.PendingPin is null ? Loc.Get("Idle.Connecting") : Loc.Get("Idle.EnterPin"),
            ReceiverState.Streaming when s.Kind == StreamKind.AudioOnly => Loc.Get("Idle.AudioOnly"),
            ReceiverState.Streaming => Loc.Get("Idle.VideoExternal"),
            ReceiverState.Starting => Loc.Get("Idle.Starting"),
            ReceiverState.Faulted => Loc.Get("Idle.Faulted"),
            _ => Loc.Get("Idle.Stopped"),
        };

        if (s.AudioActive && !prev.AudioActive) _volumeApplyPending = true;
        if (s.State == ReceiverState.Ready && prev.State != ReceiverState.Ready) _secondsSinceCheck = 15;  // check in ~5 s
        if (s.State == ReceiverState.Streaming && DiscoveryWarning.Length > 0) DiscoveryWarning = "";   // evidently found
        if (s.Recording != _lastRecording || (s.State is ReceiverState.Stopped or ReceiverState.Faulted or ReceiverState.Starting))
        {
            _lastRecording = s.Recording;
            OnRecordingStateChanged(s.Recording);
        }

        if (_host.Settings.NotificationsEnabled && _host.Settings.NotifyOnConnect
            && s.State is ReceiverState.Connecting or ReceiverState.Streaming
            && _previousState is ReceiverState.Ready)
        {
            NotificationRequested?.Invoke("DXMirror", Loc.Format("Notify.Connected", s.ClientName ?? Loc.Get("Notify.UnknownDevice")));
        }
        if (_host.Settings.NotificationsEnabled && s.State == ReceiverState.Faulted && _previousState != ReceiverState.Faulted)
        {
            NotificationRequested?.Invoke("DXMirror", Loc.Get("Notify.Faulted"));
        }
        _previousState = s.State;

        foreach (var name in new[]
                 {
                     nameof(IsRunning), nameof(HasClient), nameof(IsStreaming), nameof(ShowVideo), nameof(ShowIdlePanel),
                     nameof(ServiceStatus), nameof(ReceiverVersion), nameof(ClientName), nameof(ClientModel),
                     nameof(ConnectionStatus), nameof(Resolution), nameof(Codec), nameof(Fps), nameof(Bitrate),
                     nameof(AudioStatus), nameof(StreamKindText), nameof(PendingPin), nameof(ShowPin), nameof(Quality),
                     nameof(MissedHeartbeats), nameof(DeviceName),
                 })
        {
            OnPropertyChanged(name);
        }
        UpdateDuration();
        CommandManager.InvalidateRequerySuggested();
        StateChanged?.Invoke();
    }

    internal void RefreshFromSettings()
    {
        ApplySnapshot(_snapshot);
        UpdateNetwork();
    }

    private static (string, string, StatusTone) DescribeState(ReceiverSnapshot s) => s.State switch
    {
        ReceiverState.Stopped => (Loc.Get("Status.Inactive"), Loc.Get("Status.InactiveDetail"), StatusTone.Idle),
        ReceiverState.Starting => (Loc.Get("Status.Starting"), s.LastError is null ? "" : Loc.Get("Error." + s.LastError), StatusTone.Busy),
        ReceiverState.Ready => (Loc.Get("Status.Ready"), s.LastError == "connection_lost" ? Loc.Get("Error.connection_lost") : Loc.Get("Status.ReadyDetail"), StatusTone.Ok),
        ReceiverState.Connecting => (Loc.Get("Status.Connecting"), s.ClientName ?? "", StatusTone.Busy),
        ReceiverState.Streaming => (Loc.Get("Status.Streaming"), s.ClientName ?? "", StatusTone.Ok),
        ReceiverState.Stopping => (Loc.Get("Status.Stopping"), "", StatusTone.Busy),
        ReceiverState.Faulted => (Loc.Get("Status.Faulted"), Loc.Get("Error." + (s.LastError ?? "receiver_crashed")), StatusTone.Error),
        _ => ("", "", StatusTone.Idle),
    };

    public string StatusSummary => string.IsNullOrEmpty(StatusDetail) ? StatusTitle : $"{StatusTitle} – {StatusDetail}";

    private void UpdateDuration()
    {
        if (_snapshot.ConnectedSince is not { } since)
        {
            Duration = "–";
            return;
        }
        var d = DateTimeOffset.UtcNow - since;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        Duration = d.TotalHours >= 1
            ? d.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : d.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- diagnostics
    private string _metrics = "";
    public string Metrics { get => _metrics; private set => Set(ref _metrics, value); }
    private string _firewallText = "–";
    public string FirewallText { get => _firewallText; private set => Set(ref _firewallText, value); }
    private string _networkProfileText = "–";
    public string NetworkProfileText { get => _networkProfileText; private set => Set(ref _networkProfileText, value); }
    private bool _diagnosticsVisible;
    public bool DiagnosticsVisible
    {
        get => _diagnosticsVisible;
        set { if (Set(ref _diagnosticsVisible, value) && value) _ = RefreshDiagnosticsAsync(); }
    }

    private void OnTick()
    {
        // discovery check: a few seconds after the receiver is ready, then every 20 s while waiting for an iPhone
        _secondsSinceCheck++;
        if (State == ReceiverState.Ready && _secondsSinceCheck >= 20)
        {
            _secondsSinceCheck = 0;
            _ = RunDiscoveryCheckAsync();
        }
        else if (State is not (ReceiverState.Ready or ReceiverState.Connecting or ReceiverState.Streaming) && DiscoveryWarning.Length > 0)
        {
            DiscoveryWarning = "";
        }
        UpdateDuration();
        if (_recordPrefix is not null) UpdateRecordingText();
        if (_volumeApplyPending && _host.Receiver.ProcessId is not null) _ = ApplyVolumeNowAsync();
        if (_diagnosticsVisible) _ = SampleMetricsAsync();
    }

    private async Task SampleMetricsAsync()
    {
        var pid = _host.Receiver.ProcessId;
        var adapterId = _host.CurrentAdapter?.Id;
        var sample = await Task.Run(() => _host.Monitor.Sample(pid, adapterId));
        static string Mb(long b) => $"{b / (1024.0 * 1024.0):0} MB";
        Metrics = string.Join(Environment.NewLine,
            Loc.Format("Diag.AppUsage", sample.AppCpuPercent.ToString("0.0", CultureInfo.CurrentCulture), Mb(sample.AppWorkingSetBytes)),
            pid is null ? Loc.Get("Diag.ReceiverNotRunning")
                        : Loc.Format("Diag.ReceiverUsage", sample.ReceiverCpuPercent.ToString("0.0", CultureInfo.CurrentCulture), Mb(sample.ReceiverWorkingSetBytes)),
            Loc.Format("Diag.Network", sample.NetworkReceiveKbps >= 1000 ? $"{sample.NetworkReceiveKbps / 1000:0.0} Mbit/s" : $"{sample.NetworkReceiveKbps:0} kbit/s"),
            Loc.Format("Diag.Stream", Fps, Bitrate, _snapshot.MissedHeartbeats),
            Loc.Get("Diag.Gpu"),
            Loc.Format("Diag.LogDropped", _host.Log.DroppedEntries));
    }

    private async Task RefreshDiagnosticsAsync()
    {
        FirewallText = Loc.Get("Diag.Checking");
        var fw = await FirewallService.GetStatusAsync();
        FirewallText = Loc.Get("Firewall." + fw);
        NetworkProfileText = await NetworkProfile.QueryAsync() is { Length: > 0 } p ? p : Loc.Get("Diag.Unknown");
        await SampleMetricsAsync();
    }

    private async Task SetupFirewallAsync()
    {
        var answer = MessageBox.Show(Loc.Get("Firewall.Explain"), "DXMirror", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK) return;
        var ok = await FirewallService.InstallRulesAsync(_host.Settings.CustomUxPlayPath ?? AppPaths.ReceiverExe);
        _host.Log.Info("Firewall", ok ? "Firewall rules created (private networks only)." : "Firewall rules were not created (cancelled or failed).");
        await RefreshDiagnosticsAsync();
    }

    private async Task ExportDiagnosticsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"DXMirror-Diagnose-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Filter = "ZIP (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog() != true) return;
        var extra = new Dictionary<string, string>
        {
            ["DXMirror:"] = AppPaths.Version,
            ["UxPlay:"] = ReceiverVersion,
            ["Receiver:"] = File.Exists(AppPaths.ReceiverExe) ? AppPaths.ReceiverExe : "missing",
            ["State:"] = State.ToString(),
            ["Firewall:"] = (await FirewallService.GetStatusAsync()).ToString(),
            ["NetProfile:"] = await NetworkProfile.QueryAsync(),
            ["AudioDevs:"] = string.Join(" ; ", AudioEndpoints.GetOutputDevices().Select(d => d.Name + (d.IsDefault ? " (default)" : ""))),
        };
        var settings = _host.Settings.Clone();
        var adapters = _host.Adapters;
        var path = dialog.FileName;
        await Task.Run(() => DiagnosticsReport.Write(path, settings, _host.Log, adapters, extra));
        _host.Log.Info("Diagnostics", "Diagnostic report exported.");
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogDirectory}\"") { UseShellExecute = true });
    }

    private void AddLogLine(LogEntry e)
    {
        LogLines.Add(e.ToString());
        while (LogLines.Count > 500) LogLines.RemoveAt(0);
    }
}
