using System.Text.RegularExpressions;

namespace DXMirror.Core.Diagnostics;

/// <summary>
/// Removes authentication material from log text before it is stored or exported:
/// PIN codes, passwords, key / pairing material (long hex or base64 runs) and
/// the receiver's own status line that carries the PIN.
/// </summary>
public static partial class LogRedactor
{
    public static string Redact(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        if (line.StartsWith("@@DXM pin", StringComparison.Ordinal)) return "@@DXM pin code=[redacted]";
        var s = PinPattern().Replace(line, m => m.Groups[1].Value + "[redacted]");
        s = PasswordPattern().Replace(s, m => m.Groups[1].Value + "[redacted]");
        s = HexBlob().Replace(s, "[hex redacted]");
        s = Base64Blob().Replace(s, "[base64 redacted]");
        return s;
    }

    [GeneratedRegex(@"(?i)(\bpin\b\s*(?:code)?\s*[=:]?\s*)\d{4,8}")]
    private static partial Regex PinPattern();

    [GeneratedRegex(@"(?i)(\b(?:password|passwd|pwd|pw)\b\s*[=:]\s*)\S+")]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{32,}\b")]
    private static partial Regex HexBlob();

    [GeneratedRegex(@"[A-Za-z0-9+/]{40,}={0,2}")]
    private static partial Regex Base64Blob();
}
