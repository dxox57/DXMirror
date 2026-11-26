using System.Text.Json;
using System.Text.Json.Serialization;

namespace DXMirror.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Writes are atomic (temp file + replace)
/// so a crash or power loss during saving never leaves a truncated settings file.
/// A corrupt file is moved aside (settings.json.corrupt) and defaults are used.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();
    public string FilePath { get; }

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    private static AppSettings AfterLoad(AppSettings s) => s.Normalize();

    private static AppSettings BeforeSave(AppSettings s) => s.Clone().Normalize();

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings().Normalize();
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return AfterLoad(settings ?? new AppSettings());
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                TryQuarantine();
                return new AppSettings().Normalize();
            }
            catch (IOException)
            {
                return new AppSettings().Normalize();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(BeforeSave(settings), JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    private void TryQuarantine()
    {
        try { File.Move(FilePath, FilePath + ".corrupt", overwrite: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
