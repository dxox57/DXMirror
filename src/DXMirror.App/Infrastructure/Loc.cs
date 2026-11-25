using System.Globalization;
using System.Windows;

namespace DXMirror.App.Infrastructure;

/// <summary>
/// Runtime-switchable localisation. Strings live in Localization/Strings.de.xaml and
/// Strings.en.xaml (keys "S.*"); XAML uses {DynamicResource S.Key}, code uses <see cref="Get"/>.
/// </summary>
public static class Loc
{
    private static ResourceDictionary? _current;
    public static string Language { get; private set; } = "en";

    public static event Action? LanguageChanged;

    public static string Resolve(string setting) =>
        setting is "de" or "en"
            ? setting
            : InstallerLanguage.Value
              ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en");

    /// <summary>Language picked in Setup (install.ini next to the exe), used for "Automatic".</summary>
    private static readonly Lazy<string?> InstallerLanguage = new(() =>
    {
        try
        {
            var ini = System.IO.Path.Combine(AppContext.BaseDirectory, "install.ini");
            if (!System.IO.File.Exists(ini)) return null;
            foreach (var line in System.IO.File.ReadAllLines(ini))
            {
                var t = line.Trim();
                if (!t.StartsWith("Language=", StringComparison.OrdinalIgnoreCase)) continue;
                var v = t["Language=".Length..].Trim().ToLowerInvariant();
                return v is "de" or "en" ? v : null;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        return null;
    });

    public static void Apply(string setting)
    {
        var lang = Resolve(setting);
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/DXMirror;component/Localization/Strings.{lang}.xaml", UriKind.Absolute),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Language = lang;
        var culture = new CultureInfo(lang == "de" ? "de-DE" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource("S." + key) as string ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
