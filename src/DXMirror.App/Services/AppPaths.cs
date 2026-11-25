namespace DXMirror.App.Services;

/// <summary>Install and data locations.</summary>
internal static class AppPaths
{
    public static string InstallDirectory => AppContext.BaseDirectory;

    /// <summary>Bundled receiver (UxPlay + GStreamer runtime), see build/build-uxplay.sh.</summary>
    public static string ReceiverRoot => Path.Combine(InstallDirectory, "receiver");
    public static string ReceiverExe => Path.Combine(ReceiverRoot, "bin", "uxplay.exe");
    public static string GstPluginDirectory => Path.Combine(ReceiverRoot, "lib", "gstreamer-1.0");
    /// <summary>The scanner lives next to the DLLs in bin\ so Windows finds its dependencies.</summary>
    public static string GstPluginScanner => Path.Combine(ReceiverRoot, "bin", "gst-plugin-scanner.exe");

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DXMirror");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string ReceiverDataDirectory => Path.Combine(DataDirectory, "receiver");
    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");
    public static string GstRegistry => Path.Combine(CacheDirectory, "gstreamer-registry.bin");

    /// <summary>Default recording folder: Videos\DXMirror.</summary>
    public static string DefaultRecordingFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DXMirror");

    /// <summary>
    /// One-time migration from the project's former name: settings and the receiver key (so the iPhone
    /// keeps recognising this PC) are copied from %LOCALAPPDATA%\AirBridge if DXMirror has no data yet.
    /// </summary>
    public static void MigrateFromOldName()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Air" + "Bridge");
            if (Directory.Exists(DataDirectory) || !Directory.Exists(old)) return;
            Directory.CreateDirectory(ReceiverDataDirectory);
            CopyIfExists(Path.Combine(old, "settings.json"), SettingsFile);
            CopyIfExists(Path.Combine(old, "receiver", "uxplay-key.pem"), Path.Combine(ReceiverDataDirectory, "uxplay-key.pem"));
            CopyIfExists(Path.Combine(old, "receiver", "uxplay-register.txt"), Path.Combine(ReceiverDataDirectory, "uxplay-register.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void CopyIfExists(string from, string to)
    {
        if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
    }

    public static string Version =>
        typeof(AppPaths).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
