using System.Windows;
using System.Windows.Interop;
using DXMirror.App.Platform;
using Microsoft.Win32;

namespace DXMirror.App.Infrastructure;

/// <summary>Light / dark theme switching (brush dictionaries Themes/Light.xaml and Themes/Dark.xaml).</summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;
    public static bool IsDark { get; private set; }

    public static void Apply(string setting)
    {
        IsDark = setting switch
        {
            "dark" => true,
            "light" => false,
            _ => SystemUsesDarkTheme(),
        };
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/DXMirror;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        int dark = IsDark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = 2; // DWMWCP_ROUND (Windows 11; ignored on Windows 10)
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }
}
