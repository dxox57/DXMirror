using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DXMirror.App.Infrastructure;
using DXMirror.App.Platform;
using DXMirror.App.Services;
using DXMirror.Core.Settings;

namespace DXMirror.App.ViewModels;

public sealed record Option(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Settings page. Edits a copy of the settings; nothing is applied before "Save".</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly MainViewModel _main;
    private AppSettings _edit;

    internal SettingsViewModel(AppHost host, MainViewModel main)
    {
        _host = host;
        _main = main;
        _edit = host.Settings.Clone();
        SaveCommand = new AsyncCommand(SaveAsync, onError: ex =>
        {
            _host.Log.Error("Settings", "Saving settings failed", ex);
            MessageBox.Show(ex.Message, "DXMirror", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
        RevertCommand = new RelayCommand(Revert);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        RefreshDevicesCommand = new RelayCommand(RefreshAudioDevices);
        BrowseRecordingFolderCommand = new RelayCommand(BrowseRecordingFolder);
        OpenRecordingFolderCommand = new RelayCommand(() =>
        {
            var folder = _edit.RecordingFolder ?? AppPaths.DefaultRecordingFolder;
            try
            {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { SaveMessage = ex.Message; }
        });
        Loc.LanguageChanged += RebuildOptions;
        RebuildOptions();
        RefreshAudioDevices();
        RefreshAdapters();
    }

    /// <summary>The settings being edited (bound directly from XAML).</summary>
    public AppSettings Edit { get => _edit; private set { _edit = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecordingFolderText)); OnPropertyChanged(nameof(AdapterSelection)); OnPropertyChanged(nameof(AudioDeviceSelection)); } }

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand BrowseRecordingFolderCommand { get; }
    public ICommand OpenRecordingFolderCommand { get; }

    /// <summary>Recording folder shown in the settings (default folder when none is set).</summary>
    public string RecordingFolderText
    {
        get => _edit.RecordingFolder ?? AppPaths.DefaultRecordingFolder;
        set
        {
            var v = value?.Trim();
            _edit.RecordingFolder = string.IsNullOrEmpty(v) || string.Equals(v, AppPaths.DefaultRecordingFolder, StringComparison.OrdinalIgnoreCase) ? null : v;
            OnPropertyChanged();
        }
    }

    private void BrowseRecordingFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(RecordingFolderText) ? RecordingFolderText : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };
        if (dialog.ShowDialog() == true) RecordingFolderText = dialog.FolderName;
    }

    private string _saveMessage = "";
    public string SaveMessage { get => _saveMessage; private set => Set(ref _saveMessage, value); }

    // ---------------------------------------------------------------- option lists
    public ObservableCollection<Option> Languages { get; } = [];
    public ObservableCollection<Option> Themes { get; } = [];
    public ObservableCollection<Option> Resolutions { get; } = [];
    public ObservableCollection<Option> FrameRates { get; } = [];
    public ObservableCollection<Option> Renderers { get; } = [];
    public ObservableCollection<Option> ScalingModes { get; } = [];
    public ObservableCollection<Option> LogLevels { get; } = [];
    public ObservableCollection<Option> AdapterOptions { get; } = [];
    public ObservableCollection<Option> AudioDevices { get; } = [];

    private const string AutoValue = "__auto__";

    private void RebuildOptions()
    {
        // ComboBoxes bound to Edit.* write null back while their items are cleared: keep the values
        var (lang, theme, renderer, scaling, logLevel) = (_edit.Language, _edit.Theme, _edit.VideoRenderer, _edit.Scaling, _edit.LogLevel);
        RebuildOptionLists();
        (_edit.Language, _edit.Theme, _edit.VideoRenderer, _edit.Scaling, _edit.LogLevel) = (lang, theme, renderer, scaling, logLevel);
        OnPropertyChanged(nameof(Edit));
        OnPropertyChanged(nameof(ResolutionSelection));
        OnPropertyChanged(nameof(FpsSelection));
    }

    private void RebuildOptionLists()
    {
        Fill(Languages, ("auto", Loc.Get("Opt.LangAuto")), ("de", "Deutsch"), ("en", "English"));
        Fill(Themes, ("system", Loc.Get("Opt.System")), ("light", Loc.Get("Opt.Light")), ("dark", Loc.Get("Opt.Dark")));
        Fill(Resolutions, ("1280x720", "1280 × 720 (720p)"), ("1920x1080", "1920 × 1080 (1080p)"), ("2560x1440", "2560 × 1440"), ("3840x2160", "3840 × 2160 (4K, H.265)"));
        Fill(FrameRates, ("30", "30 FPS"), ("60", "60 FPS"));
        Fill(Renderers, ("d3d11", "Direct3D 11"), ("d3d12", "Direct3D 12"));
        Fill(ScalingModes, ("fit", Loc.Get("Opt.ScaleFit")), ("stretch", Loc.Get("Opt.ScaleStretch")));
        Fill(LogLevels, ("info", Loc.Get("Opt.LogInfo")), ("debug", Loc.Get("Opt.LogDebug")));
    }

    private static void Fill(ObservableCollection<Option> target, params (string Value, string Label)[] items)
    {
        target.Clear();
        foreach (var (v, l) in items) target.Add(new Option(v, l));
    }

    public void RefreshAdapters()
    {
        AdapterOptions.Clear();
        AdapterOptions.Add(new Option(AutoValue, Loc.Get("Opt.AutoAdapter")));
        foreach (var a in _host.Adapters.Where(a => a.IsUp && a.PrimaryIPv4 is not null))
            AdapterOptions.Add(new Option(a.Id, $"{a.Name} – {a.PrimaryIPv4} ({Loc.Get("Network.Kind." + a.Kind)})"));
        OnPropertyChanged(nameof(AdapterSelection));
    }

    private void RefreshAudioDevices()
    {
        AudioDevices.Clear();
        AudioDevices.Add(new Option(AutoValue, Loc.Get("Opt.DefaultAudio")));
        foreach (var d in AudioEndpoints.GetOutputDevices())
            AudioDevices.Add(new Option(d.Id, d.IsDefault ? d.Name + " ★" : d.Name));
        OnPropertyChanged(nameof(AudioDeviceSelection));
    }

    // ---------------------------------------------------------------- composite bindings
    // Note: a ComboBox pushes null into SelectedValue while its item list is being rebuilt – ignore that.
    public string? AdapterSelection
    {
        get => _edit.AutoSelectAdapter || _edit.NetworkAdapterId is null ? AutoValue : _edit.NetworkAdapterId;
        set
        {
            if (value is null) return;
            if (value == AutoValue) { _edit.AutoSelectAdapter = true; _edit.NetworkAdapterId = null; }
            else { _edit.AutoSelectAdapter = false; _edit.NetworkAdapterId = value; }
            OnPropertyChanged();
        }
    }

    public string? AudioDeviceSelection
    {
        get => _edit.AudioOutputDeviceId ?? AutoValue;
        set
        {
            if (value is null) return;
            _edit.AudioOutputDeviceId = value == AutoValue ? null : value;
            OnPropertyChanged();
        }
    }

    public string? ResolutionSelection
    {
        get => $"{_edit.MaxWidth}x{_edit.MaxHeight}";
        set
        {
            if (value is null) return;
            var parts = value.Split('x');
            if (parts is { Length: 2 } && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            {
                _edit.MaxWidth = w;
                _edit.MaxHeight = h;
                if (h > 1440) _edit.EnableH265 = true; // 4K is only offered by iOS with H.265
                OnPropertyChanged(nameof(Edit));
            }
            OnPropertyChanged();
        }
    }

    public string? FpsSelection
    {
        get => _edit.MaxFps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set { if (value is not null && int.TryParse(value, out var f)) { _edit.MaxFps = f; OnPropertyChanged(); } }
    }

    public string AppVersion => AppPaths.Version;
    public string DataFolder => AppPaths.DataDirectory;

    // ---------------------------------------------------------------- actions
    private async Task SaveAsync()
    {
        var updated = _edit.Clone().Normalize();
        var old = _host.Settings;

        if (updated.StartWithWindows != AutostartService.IsEnabled())
        {
            try { AutostartService.SetEnabled(updated.StartWithWindows); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _host.Log.Warn("Settings", "Autostart could not be changed: " + ex.Message);
                updated.StartWithWindows = AutostartService.IsEnabled();
            }
        }

        await _host.ApplySettingsAsync(updated);
        if (old.Theme != updated.Theme) ThemeManager.Apply(updated.Theme);
        if (old.Language != updated.Language) Loc.Apply(updated.Language);
        Edit = _host.Settings.Clone();
        OnPropertyChanged(nameof(ResolutionSelection));
        OnPropertyChanged(nameof(FpsSelection));
        _main.RefreshFromSettings();
        SaveMessage = Loc.Get("Settings.Saved");
    }

    private void Revert()
    {
        Edit = _host.Settings.Clone();
        OnPropertyChanged(nameof(ResolutionSelection));
        OnPropertyChanged(nameof(FpsSelection));
        SaveMessage = "";
    }

    private void ResetDefaults()
    {
        var fresh = new AppSettings().Normalize();
        // keep things the user would not expect to lose
        fresh.Language = _edit.Language;
        fresh.Theme = _edit.Theme;
        Edit = fresh;
        OnPropertyChanged(nameof(ResolutionSelection));
        OnPropertyChanged(nameof(FpsSelection));
        SaveMessage = Loc.Get("Settings.DefaultsLoaded");
    }
}
