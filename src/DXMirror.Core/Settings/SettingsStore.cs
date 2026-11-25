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

    private readonly Func<string, string>? _protect;
    private readonly Func<string, string?>? _unprotect;
    private const string ProtectedPrefix = "protected:";

    /// <param name="protect">Optional encryption for secret values (the fixed PIN), e.g. Windows DPAPI.</param>
    /// <param name="unprotect">Inverse of <paramref name="protect"/>; returns null if the value cannot be decrypted.</param>
    public SettingsStore(string filePath, Func<string, string>? protect = null, Func<string, string?>? unprotect = null)
    {
        FilePath = filePath;
        _protect = protect;
        _unprotect = unprotect;
    }

    private AppSettings AfterLoad(AppSettings s)
    {
        if (s.FixedPin is { } pin && pin.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            s.FixedPin = _unprotect?.Invoke(pin[ProtectedPrefix.Length..]);
        }
        return s.Normalize();
    }

    private AppSettings BeforeSave(AppSettings s)
    {
        var copy = s.Clone().Normalize();
        if (_protect is not null && !string.IsNullOrEmpty(copy.FixedPin))
            copy.FixedPin = ProtectedPrefix + _protect(copy.FixedPin);
        return copy;
    }

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
