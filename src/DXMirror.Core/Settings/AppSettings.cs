namespace DXMirror.Core.Settings;

/// <summary>
/// All persisted user settings. Plain POCO so it serialises with System.Text.Json.
/// Every value is validated by <see cref="Normalize"/> after loading, so a hand-edited
/// or corrupted settings file can never produce an invalid receiver configuration.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // ---------------- General ----------------
    public string DeviceName { get; set; } = DefaultDeviceName();
    /// <summary>"auto", "de" or "en".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    public bool StartWithWindows { get; set; }
    public bool StartReceiverOnLaunch { get; set; } = true;
    /// <summary>Minimising hides the window into the tray (no taskbar button). Off = normal minimise.</summary>
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;

    // ---------------- Connection ----------------
    public bool AutoSelectAdapter { get; set; } = true;
    /// <summary>NetworkInterface.Id of the adapter chosen by the user (used when AutoSelectAdapter is false).</summary>
    public string? NetworkAdapterId { get; set; }
    public bool NotifyOnConnect { get; set; } = true;
    /// <summary>Restart the receiver automatically after a crash or a lost connection.</summary>
    public bool AutoReconnect { get; set; } = true;
    /// <summary>Seconds of client silence before UxPlay resets the session (UxPlay -reset n).</summary>
    public int ConnectionTimeoutSeconds { get; set; } = 15;
    /// <summary>A new iPhone takes over an existing session (UxPlay -nohold).</summary>
    public bool NewClientTakesOver { get; set; } = true;

    // ---------------- Video ----------------
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;
    public int MaxFps { get; set; } = 60;
    public bool HardwareAcceleration { get; set; } = true;
    /// <summary>"d3d11" or "d3d12" GStreamer video sink.</summary>
    public string VideoRenderer { get; set; } = "d3d11";
    /// <summary>"fit" (keep aspect ratio) or "stretch".</summary>
    public string Scaling { get; set; } = "fit";
    /// <summary>Open the video automatically in full screen when mirroring starts.</summary>
    public bool FullscreenOnConnect { get; set; }
    /// <summary>Show the video inside the DXMirror window (true) or in a separate UxPlay window (false).</summary>
    public bool EmbedVideo { get; set; } = true;
    /// <summary>Audio-only receiver: no video window at all (AirPlay music streaming).</summary>
    public bool AudioOnlyMode { get; set; }
    /// <summary>Allow HEVC/H.265 (only useful above 1080p).</summary>
    public bool EnableH265 { get; set; }

    // ---------------- Audio ----------------
    /// <summary>Windows endpoint id (e.g. "{0.0.0.00000000}.{guid}"); null = Windows default device.</summary>
    public string? AudioOutputDeviceId { get; set; }
    /// <summary>0.0 – 1.0, applied to the receiver's Windows audio session.</summary>
    public double Volume { get; set; } = 1.0;
    public bool Muted { get; set; }
    /// <summary>Audio latency reported to the iPhone in milliseconds (UxPlay -al; UxPlay default 250,
    /// DXMirror uses 100 for lower delay – tested with iPhone 16 Pro, lip-sync stays correct).</summary>
    public int AudioLatencyMs { get; set; } = 100;
    /// <summary>Extra audio delay in ms for lip-sync fine tuning (UxPlay -vsync x); may be negative.</summary>
    public int AvSyncOffsetMs { get; set; }
    /// <summary>Timestamp-based A/V sync (UxPlay -vsync). Off by default: every frame is shown as soon
    /// as it is decoded, which noticeably lowers the mirroring delay.</summary>
    public bool AvSyncEnabled { get; set; }
    public bool WasapiLowLatency { get; set; }

    // ---------------- Recording ----------------
    /// <summary>Folder for MP4 recordings. Null = default (Videos\DXMirror).</summary>
    public string? RecordingFolder { get; set; }

    // ---------------- Advanced ----------------
    /// <summary>"info" or "debug".</summary>
    public string LogLevel { get; set; } = "info";
    public bool DebugMode { get; set; }
    /// <summary>Only for experts: path to a custom uxplay.exe. Null = bundled.</summary>
    public string? CustomUxPlayPath { get; set; }

    public static string DefaultDeviceName()
    {
        var machine = Environment.MachineName;
        return string.IsNullOrWhiteSpace(machine) ? "DXMirror – Mein PC" : $"DXMirror – {machine}";
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Clamp and sanitise all values. Returns this for chaining.</summary>
    public AppSettings Normalize()
    {
        DeviceName = DeviceNameRules.Sanitize(DeviceName);
        Language = Language is "de" or "en" ? Language : "auto";
        Theme = Theme is "light" or "dark" ? Theme : "system";

        ConnectionTimeoutSeconds = Math.Clamp(ConnectionTimeoutSeconds, 0, 300);

        MaxWidth = Math.Clamp(MaxWidth, 320, 3840);
        MaxHeight = Math.Clamp(MaxHeight, 240, 2160);
        MaxFps = MaxFps is 24 or 30 or 60 ? MaxFps : (MaxFps < 45 ? 30 : 60);
        VideoRenderer = VideoRenderer is "d3d12" ? "d3d12" : "d3d11";
        Scaling = Scaling is "stretch" ? "stretch" : "fit";

        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0.0, 1.0) : 1.0;
        AudioLatencyMs = Math.Clamp(AudioLatencyMs, 50, 1000);
        AvSyncOffsetMs = Math.Clamp(AvSyncOffsetMs, -1000, 1000);

        LogLevel = LogLevel is "debug" ? "debug" : "info";
        if (string.IsNullOrWhiteSpace(CustomUxPlayPath)) CustomUxPlayPath = null;
        RecordingFolder = string.IsNullOrWhiteSpace(RecordingFolder) ? null : RecordingFolder.Trim();
        if (string.IsNullOrWhiteSpace(AudioOutputDeviceId)) AudioOutputDeviceId = null;
        if (string.IsNullOrWhiteSpace(NetworkAdapterId)) NetworkAdapterId = null;
        SchemaVersion = CurrentSchemaVersion;
        return this;
    }
}

public static class DeviceNameRules
{
    public const int MaxLength = 50;

    /// <summary>
    /// AirPlay names travel in mDNS TXT/instance labels (max 63 bytes UTF-8). We remove control
    /// characters and the characters that would break UxPlay's startup-file tokenizer, and cap
    /// the length so the UTF-8 encoding stays well below the mDNS label limit.
    /// </summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return AppSettings.DefaultDeviceName();
        var chars = name.Where(c => !char.IsControl(c) && c != '"' && c != '\\' && c != '@' && c != '.').ToArray();
        var cleaned = new string(chars).Trim();
        while (cleaned.Contains("  ", StringComparison.Ordinal)) cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        if (cleaned.Length == 0) return AppSettings.DefaultDeviceName();
        // cap by UTF-8 bytes (mDNS label limit 63 bytes)
        while (cleaned.Length > MaxLength || System.Text.Encoding.UTF8.GetByteCount(cleaned) > 60)
            cleaned = cleaned[..^1].TrimEnd();
        return cleaned;
    }
}
