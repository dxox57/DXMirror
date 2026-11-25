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

    public static async Task<FirewallStatus> GetStatusAsync()
    {
        try
        {
            var tcp = await RunNetshAsync($"advfirewall firewall show rule name=\"{RuleNameTcp}\"").ConfigureAwait(false);
            var udp = await RunNetshAsync($"advfirewall firewall show rule name=\"{RuleNameUdp}\"").ConfigureAwait(false);
            return tcp == 0 && udp == 0 ? FirewallStatus.RulesPresent : FirewallStatus.RulesMissing;
        }
        catch (Win32Exception) { return FirewallStatus.Unknown; }
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

    private static async Task<int> RunNetshAsync(string args)
    {
        var psi = new ProcessStartInfo("netsh.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().ConfigureAwait(false);
        return p.ExitCode;
    }
}
