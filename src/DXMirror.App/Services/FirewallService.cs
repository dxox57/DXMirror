using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DXMirror.App.Services;

public enum FirewallStatus
{
    Unknown,
    RulesPresent,
    RulesMissing,
}

/// <summary>
/// Manages the Windows Defender Firewall rules DXMirror needs. Rules are program-scoped
/// (only the bundled receiver), inbound only, and limited to the "Private" network profile.
/// Changing rules needs administrator rights, so this always goes through a UAC prompt that the
/// user has confirmed in the UI before. DXMirror itself keeps running without elevation.
/// </summary>
internal static class FirewallService
{
    public const string RuleNameTcp = "DXMirror Receiver (TCP-In)";
    public const string RuleNameUdp = "DXMirror Receiver (UDP-In)";

    /// <summary>
    /// Both rules must exist and point to <paramref name="receiverExe"/>; rules left over from an
    /// installation in another folder (e.g. the former "Program Files" install) count as missing.
    /// Reading rules needs no administrator rights.
    /// </summary>
    public static async Task<FirewallStatus> GetStatusAsync(string receiverExe)
    {
        try
        {
            var tcp = await RunNetshAsync($"advfirewall firewall show rule name=\"{RuleNameTcp}\" verbose").ConfigureAwait(false);
            var udp = await RunNetshAsync($"advfirewall firewall show rule name=\"{RuleNameUdp}\" verbose").ConfigureAwait(false);
            var tail = PathTail(receiverExe);
            bool ok(int code, string output) => code == 0 && output.Contains(tail, StringComparison.OrdinalIgnoreCase);
            return ok(tcp.Code, tcp.Output) && ok(udp.Code, udp.Output) ? FirewallStatus.RulesPresent : FirewallStatus.RulesMissing;
        }
        catch (Win32Exception) { return FirewallStatus.Unknown; }
    }

    /// <summary>
    /// Part of the path that is safe to compare against netsh output: netsh prints in the console
    /// code page, so the user-name part (which may contain umlauts) is left out.
    /// </summary>
    internal static string PathTail(string exe)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0 && exe.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
            return exe[profile.Length..];
        return exe;
    }

    /// <summary>Creates (or re-creates) the rules for <paramref name="receiverExe"/>. Shows a UAC prompt.</summary>
    public static Task<bool> InstallRulesAsync(string receiverExe) => RunElevatedScriptAsync(BuildInstallCommands(receiverExe));

    public static Task<bool> RemoveRulesAsync() => RunElevatedScriptAsync(BuildRemoveCommands().TrimEnd(' ', '&'));

    internal static string BuildInstallCommands(string receiverExe)
    {
        var sb = new StringBuilder(BuildRemoveCommands());
        foreach (var (name, proto) in new[] { (RuleNameTcp, "TCP"), (RuleNameUdp, "UDP") })
        {
            sb.Append("netsh advfirewall firewall add rule ")
              .Append($"name=\"{name}\" dir=in action=allow program=\"{receiverExe}\" ")
              .Append($"protocol={proto} profile=private enable=yes ")
              .Append("description=\"AirPlay receiver (DXMirror). Local network only.\"")
              .Append(" & ");
        }
        return sb.ToString().TrimEnd(' ', '&');
    }

    internal static string BuildRemoveCommands() =>
        $"netsh advfirewall firewall delete rule name=\"{RuleNameTcp}\" >nul 2>&1 & " +
        $"netsh advfirewall firewall delete rule name=\"{RuleNameUdp}\" >nul 2>&1 & ";

    private static async Task<bool> RunElevatedScriptAsync(string commands)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c " + commands)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync().ConfigureAwait(false);
            return p.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // user cancelled the UAC prompt
            return false;
        }
    }

    private static async Task<(int Code, string Output)> RunNetshAsync(string args)
    {
        var psi = new ProcessStartInfo("netsh.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().ConfigureAwait(false);
        return (p.ExitCode, await stdout.ConfigureAwait(false));
    }
}
