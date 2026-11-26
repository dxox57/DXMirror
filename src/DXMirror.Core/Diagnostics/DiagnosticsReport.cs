using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DXMirror.Core.Network;
using DXMirror.Core.Settings;

namespace DXMirror.Core.Diagnostics;

/// <summary>
/// Builds the "Diagnosebericht exportieren" ZIP: system info, network adapters, sanitised settings
/// resource metrics and the rotated log files. Contains no video/audio content.
/// </summary>
public static class DiagnosticsReport
{
    public static void Write(
        string zipPath,
        AppSettings settings,
        AppLog log,
        IEnumerable<NetworkAdapterInfo> adapters,
        IReadOnlyDictionary<string, string> extraInfo)
    {
        var dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var sys = new StringBuilder();
        sys.AppendLine($"Created:      {DateTimeOffset.Now:O}");
        sys.AppendLine($"OS:           {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sys.AppendLine($"Runtime:      {RuntimeInformation.FrameworkDescription}");
        sys.AppendLine($"Process arch: {RuntimeInformation.ProcessArchitecture}");
        sys.AppendLine($"CPU count:    {Environment.ProcessorCount}");
        foreach (var (k, v) in extraInfo) sys.AppendLine($"{k,-13} {LogRedactor.Redact(v)}");
        AddText(zip, "system.txt", sys.ToString());

        var net = new StringBuilder();
        foreach (var a in adapters)
        {
            net.AppendLine($"{a.Name} | {a.Description} | {a.Kind} | up={a.IsUp} | gw={a.HasGateway} | " +
                           $"ipv4={string.Join(",", a.IPv4Addresses)} | ipv6={string.Join(",", a.IPv6Addresses)} | score={a.SuitabilityScore}");
        }
        AddText(zip, "network.txt", net.ToString());

        AddText(zip, "settings.json", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        var recent = new StringBuilder();
        foreach (var e in log.Recent()) recent.AppendLine(e.ToString());
        AddText(zip, "log-recent.txt", recent.ToString());

        if (log.Directory is { } logDir && Directory.Exists(logDir))
        {
            foreach (var file in Directory.EnumerateFiles(logDir, "dxmirror*.log"))
            {
                try
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs);
                    // files are already redacted when written; redact again defensively
                    var sb = new StringBuilder();
                    string? line;
                    while ((line = reader.ReadLine()) is not null) sb.AppendLine(LogRedactor.Redact(line));
                    AddText(zip, "logs/" + Path.GetFileName(file), sb.ToString());
                }
                catch (IOException) { }
            }
        }
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
