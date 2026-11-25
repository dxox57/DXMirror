using DXMirror.App.Platform;
using DXMirror.Core.Diagnostics;
using DXMirror.Core.Network;
using DXMirror.Core.Receiver;
using DXMirror.Core.Settings;

namespace DXMirror.App.Services;

/// <summary>
/// Composition root: owns settings, logging, the receiver controller and the network watcher.
/// Lives for the whole application lifetime. No UI types in here.
/// </summary>
internal sealed class AppHost : IAsyncDisposable
{
    private const string Src = "App";
    private readonly ChildProcessJob _job = new();
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private string? _lastMdnsIp;
    private Func<nint> _videoHandle = () => 0;

    public AppLog Log { get; }
    public SettingsStore Store { get; }
    public AppSettings Settings { get; private set; }
    public ReceiverController Receiver { get; }
    public NetworkAdapterService Network { get; }
    public ResourceMonitor Monitor { get; } = new();
    public NetworkAdapterInfo? CurrentAdapter { get; private set; }
    public IReadOnlyList<NetworkAdapterInfo> Adapters { get; private set; } = [];

    public event Action? NetworkChanged;

    public AppHost()
    {
        AppPaths.MigrateFromOldName();
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Directory.CreateDirectory(AppPaths.CacheDirectory);
        Log = new AppLog(AppPaths.LogDirectory);
        Store = new SettingsStore(AppPaths.SettingsFile, NativeMethods.ProtectString, NativeMethods.UnprotectString);
        Settings = Store.Load();
        if (Settings.DeviceName.StartsWith("Air" + "Bridge", StringComparison.OrdinalIgnoreCase))
        {
            // device name from the project's former name
            Settings.DeviceName = "DXMirror" + Settings.DeviceName["AirBridge".Length..];
            Store.Save(Settings);
        }
        Log.MinimumLevel = Settings.LogLevel == "debug" || Settings.DebugMode ? LogLevel.Debug : LogLevel.Info;
        Log.Info(Src, $"DXMirror {AppPaths.Version} starting on {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")}).");

        var factory = new SystemReceiverProcessFactory { OnProcessStarted = p => _job.Assign(p) };
        Receiver = new ReceiverController(factory, Log, new ReceiverEnvironment(
            AppPaths.ReceiverExe,
            AppPaths.ReceiverDataDirectory,
            BuildGStreamerEnvironment(),
            NativeMethods.ToAnsiSafePath));

        Network = new NetworkAdapterService();
        Network.Changed += (_, _) => _ = OnNetworkChangedAsync();
        RefreshAdapters(logAll: true);
    }

    public void SetVideoHandleProvider(Func<nint> provider) => _videoHandle = provider;

    private IReadOnlyDictionary<string, string> BuildGStreamerEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // only use the GStreamer plugins shipped with DXMirror, never a system-wide installation
            ["GST_PLUGIN_SYSTEM_PATH_1_0"] = AppPaths.GstPluginDirectory,
            ["GST_PLUGIN_PATH_1_0"] = "",
            ["GST_PLUGIN_SCANNER_1_0"] = AppPaths.GstPluginScanner,
            ["GST_REGISTRY_1_0"] = AppPaths.GstRegistry,
            ["GST_DEBUG_NO_COLOR"] = "1",
        };
        if (Settings.DebugMode) env["GST_DEBUG"] = "2";
        return env;
    }

    public void RefreshAdapters(bool logAll = false)
    {
        Adapters = NetworkAdapterService.GetAdapters();
        CurrentAdapter = NetworkAdapterService.Select(Adapters, Settings.AutoSelectAdapter, Settings.NetworkAdapterId);
        if (logAll)
        {
            foreach (var a in Adapters)
                Log.Info("Network", $"Adapter \"{a.Name}\" ({a.Kind}, up={a.IsUp}, gw={a.HasGateway}, ipv4={a.PrimaryIPv4 ?? "-"}).");
        }
        Log.Info("Network", CurrentAdapter is null
            ? "No suitable network adapter found."
            : $"Using adapter \"{CurrentAdapter.Name}\" ({CurrentAdapter.PrimaryIPv4}).");
    }

    public async Task StartReceiverAsync()
    {
        RefreshAdapters();
        _lastMdnsIp = CurrentAdapter?.PrimaryIPv4;
        // Only pin the mDNS address when the user selected an adapter or there are several candidates.
        string? mdnsIp = Settings.AutoSelectAdapter && Adapters.Count(a => a.SuitabilityScore > 0) <= 1 ? null : _lastMdnsIp;
        await Receiver.StartAsync(new ReceiverStartRequest(Settings.Clone(), _videoHandle(), mdnsIp)).ConfigureAwait(false);
    }

    public Task StopReceiverAsync() => Receiver.StopAsync();

    public Task DisconnectAsync() => Receiver.DisconnectClientAsync();

    /// <summary>Saves new settings and restarts the receiver if a receiver-relevant value changed.</summary>
    public async Task ApplySettingsAsync(AppSettings updated)
    {
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var old = Settings;
            Settings = updated.Clone().Normalize();
            Store.Save(Settings);
            Log.MinimumLevel = Settings.LogLevel == "debug" || Settings.DebugMode ? LogLevel.Debug : LogLevel.Info;
            Log.Info(Src, "Settings saved.");
            if (ReceiverRelevantChanged(old, Settings) && Receiver.IsRunning)
            {
                Log.Info(Src, "Receiver settings changed – restarting receiver.");
                await StartReceiverAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>Persists volume/mute without restarting anything.</summary>
    public void SaveVolume(double volume, bool muted)
    {
        Settings.Volume = volume;
        Settings.Muted = muted;
        try { Store.Save(Settings); } catch (IOException ex) { Log.Warn(Src, "Could not save volume: " + ex.Message); }
    }

    internal static bool ReceiverRelevantChanged(AppSettings a, AppSettings b) =>
        a.DeviceName != b.DeviceName || a.AutoSelectAdapter != b.AutoSelectAdapter || a.NetworkAdapterId != b.NetworkAdapterId
        || a.PinEnabled != b.PinEnabled || a.FixedPin != b.FixedPin || a.RememberPinClients != b.RememberPinClients
        || a.ConnectionTimeoutSeconds != b.ConnectionTimeoutSeconds || a.NewClientTakesOver != b.NewClientTakesOver
        || a.MaxWidth != b.MaxWidth || a.MaxHeight != b.MaxHeight || a.MaxFps != b.MaxFps
        || a.HardwareAcceleration != b.HardwareAcceleration || a.VideoRenderer != b.VideoRenderer || a.Scaling != b.Scaling
        || a.EmbedVideo != b.EmbedVideo || a.AudioOnlyMode != b.AudioOnlyMode || a.EnableH265 != b.EnableH265
        || a.AudioOutputDeviceId != b.AudioOutputDeviceId || a.AudioLatencyMs != b.AudioLatencyMs
        || a.AvSyncOffsetMs != b.AvSyncOffsetMs || a.AvSyncEnabled != b.AvSyncEnabled || a.WasapiLowLatency != b.WasapiLowLatency
        || a.DebugMode != b.DebugMode || a.LogLevel != b.LogLevel || a.CustomUxPlayPath != b.CustomUxPlayPath;

    /// <summary>Applies the in-app volume inside the receiver (DXMirror-patched UxPlay, stdin "volume x").</summary>
    public Task<int> ApplyVolumeAsync()
    {
        // Volume is applied inside the receiver (GStreamer volume element) via its control channel.
        Receiver.SetVolume(Settings.Volume, Settings.Muted);
        return Task.FromResult(1);
    }

    private async Task OnNetworkChangedAsync()
    {
        try
        {
            RefreshAdapters();
            NetworkChanged?.Invoke();
            var ip = CurrentAdapter?.PrimaryIPv4;
            if (Receiver.IsRunning && ip != _lastMdnsIp && !Receiver.Snapshot.HasClient)
            {
                Log.Info("Network", $"Network address changed ({_lastMdnsIp ?? "-"} → {ip ?? "-"}) – re-announcing receiver.");
                await StartReceiverAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Network", "Handling network change failed", ex);
        }
    }

    /// <summary>Removes DXMirror's own traces (used by the uninstaller via "--cleanup").</summary>
    public static void CleanupUserData(bool removeData)
    {
        try { AutostartService.SetEnabled(false); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        if (removeData && Directory.Exists(AppPaths.DataDirectory))
        {
            try { Directory.Delete(AppPaths.DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(AppPaths.CacheDirectory)) Directory.Delete(AppPaths.CacheDirectory, recursive: true);
            Directory.CreateDirectory(AppPaths.CacheDirectory);
            Log.Info(Src, "Cache cleared.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(Src, "Could not clear cache: " + ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Log.Info(Src, "DXMirror shutting down.");
        await Receiver.DisposeAsync().ConfigureAwait(false);
        Network.Dispose();
        _job.Dispose();
        await Log.DisposeAsync().ConfigureAwait(false);
        _applyGate.Dispose();
    }
}
