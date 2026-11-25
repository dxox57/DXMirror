using Microsoft.Win32;

namespace DXMirror.App.Services;

/// <summary>Start with Windows via HKCU\...\Run (per user, no admin rights needed).</summary>
internal static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DXMirror";
    public const string MinimizedArgument = "--autostart";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string s && s.Contains(Environment.ProcessPath ?? "DXMirror.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown");
            key.SetValue(ValueName, $"\"{exe}\" {MinimizedArgument}", RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
