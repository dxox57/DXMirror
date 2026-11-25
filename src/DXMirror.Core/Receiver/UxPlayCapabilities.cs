using System.Text.RegularExpressions;

namespace DXMirror.Core.Receiver;

/// <summary>
/// The options a concrete uxplay.exe supports, read from its "-h" output. DXMirror never passes an
/// option the binary does not list, so a different UxPlay version degrades gracefully instead of
/// refusing to start.
/// </summary>
public sealed partial class UxPlayCapabilities
{
    private readonly HashSet<string> _options;

    public string? Version { get; }
    public bool HasDXMirrorPatch { get; }
    public IReadOnlyCollection<string> Options => _options;

    private UxPlayCapabilities(HashSet<string> options, string? version, bool patched)
    {
        _options = options;
        Version = version;
        HasDXMirrorPatch = patched;
    }

    /// <summary>Capabilities used when the help text could not be read: options of UxPlay 1.74.</summary>
    public static UxPlayCapabilities Assume174() => new(new HashSet<string>(StringComparer.Ordinal)
    {
        "n", "nh", "h265", "pin", "reg", "pw", "vsync", "async", "db", "taper", "vol", "s", "o", "fs", "p",
        "avdec", "vp", "vd", "vc", "vs", "as", "al", "reset", "nofreeze", "nc", "nohold", "restrict", "allow",
        "block", "FPSdata", "fps", "f", "r", "m", "key", "dacp", "d", "v", "h", "rc",
    }, "1.74", false);

    /// <summary>Accept everything (used in unit tests).</summary>
    public static UxPlayCapabilities All() => new(new HashSet<string>(StringComparer.Ordinal), null, true) { };

    public bool Supports(string option) => _options.Count == 0 || _options.Contains(option);

    public static UxPlayCapabilities Parse(string helpText, bool patched)
    {
        var options = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in OptionLine().Matches(helpText))
        {
            options.Add(m.Groups[1].Value);
        }
        // "-pin[xxxx]" is printed without a space
        if (helpText.Contains("-pin[", StringComparison.Ordinal)) options.Add("pin");
        var v = VersionLine().Match(helpText);
        if (options.Count < 10)
        {
            // help text unreadable or truncated: fall back to the documented 1.74 option set
            var fallback = Assume174();
            return new UxPlayCapabilities(new HashSet<string>(fallback.Options, StringComparer.Ordinal),
                v.Success ? v.Groups[1].Value : fallback.Version, patched);
        }
        return new UxPlayCapabilities(options, v.Success ? v.Groups[1].Value : null, patched);
    }

    [GeneratedRegex(@"^-([A-Za-z][A-Za-z0-9]*)(?=[\s\[]|$)", RegexOptions.Multiline)]
    private static partial Regex OptionLine();

    [GeneratedRegex(@"UxPlay\s+(\d+\.\d+(?:\.\d+)?)")]
    private static partial Regex VersionLine();
}
