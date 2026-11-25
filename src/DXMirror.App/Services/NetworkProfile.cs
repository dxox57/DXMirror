using System.Diagnostics;

namespace DXMirror.App.Services;

/// <summary>
/// Reads the Windows network category (Private / Public / DomainAuthenticated) per connection.
/// On "Public" networks Windows blocks the discovery traffic AirPlay needs.
/// </summary>
internal static class NetworkProfile
{
    public static async Task<string> QueryAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                "\"Get-NetConnectionProfile | ForEach-Object { $_.InterfaceAlias + ': ' + $_.NetworkCategory }\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
                return "";
            }
            var text = (await output.ConfigureAwait(false)).Trim();
            return string.Join("; ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }
}
