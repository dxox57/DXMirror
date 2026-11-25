using System.Globalization;

namespace DXMirror.Core.Receiver;

/// <summary>
/// One machine-readable status line emitted by the DXMirror-patched UxPlay
/// ("@@DXM &lt;name&gt; key=value key="quoted value" ..."). See third_party/uxplay/dxmirror-uxplay.patch.
/// </summary>
public sealed record UxPlayEvent(string Name, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public int? GetInt(string key) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public long? GetLong(string key) =>
        long.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public double? GetDouble(string key) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

public static class UxPlayEventParser
{
    public const string Prefix = "@@DXM ";
    private const int MaxLineLength = 1024;

    /// <summary>
    /// Parses a stdout line. Returns null for normal log lines and for malformed / oversized input
    /// (the receiver process is treated as untrusted input: it relays data sent by the iPhone).
    /// </summary>
    public static UxPlayEvent? TryParse(string? line)
    {
        if (line is null || line.Length > MaxLineLength) return null;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var body = line.AsSpan(Prefix.Length).TrimEnd();
        if (body.IsEmpty) return null;

        int i = 0;
        string name = ReadBareToken(body, ref i);
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (i < body.Length)
        {
            SkipSpaces(body, ref i);
            if (i >= body.Length) break;
            int eq = body[i..].IndexOf('=');
            if (eq <= 0) return null;
            var key = body.Slice(i, eq).ToString();
            if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return null;
            i += eq + 1;
            string value;
            if (i < body.Length && body[i] == '"')
            {
                int close = body[(i + 1)..].IndexOf('"');
                if (close < 0) return null;
                value = body.Slice(i + 1, close).ToString();
                i += close + 2;
            }
            else
            {
                value = ReadBareToken(body, ref i);
            }
            values[key] = value;
        }
        return new UxPlayEvent(name, values);
    }

    private static string ReadBareToken(ReadOnlySpan<char> s, ref int i)
    {
        int start = i;
        while (i < s.Length && s[i] != ' ') i++;
        return s[start..i].ToString();
    }

    private static void SkipSpaces(ReadOnlySpan<char> s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
    }
}
