using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DXMirror.App.Platform;
using DXMirror.App.Infrastructure;
using DXMirror.App.Services;
using DXMirror.App.ViewModels;

namespace DXMirror.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppHost _host;
    private bool _allowClose;
    private bool _fullscreen;
    private WindowState _restoreState;
    private WindowStyle _restoreStyle;
    private ResizeMode _restoreResize;
    private HwndSource? _source;
    private bool _hotkeysRegistered;
    private const int HotkeyEscape = 0xD01;
    private const int HotkeyF11 = 0xD02;

    public event Action? CloseRequested;

    internal MainWindow(MainViewModel vm, AppHost host)
    {
        _vm = vm;
        _host = host;
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyTitleBar(this);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
        };
        Activated += (_, _) => { if (_fullscreen) SetFullscreenHotkeys(true); };
        Deactivated += OnDeactivated;
        vm.FullscreenToggleRequested += ToggleFullscreen;
        vm.PropertyChanged += OnVmPropertyChanged;
        StateChanged += OnWindowStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>HWND of the embedded video surface (0 before the window was shown).</summary>
    public nint VideoHandle => DashboardPage.VideoHandle;

    public void ShowPage(string page)
    {
        switch (page)
        {
            case "settings": NavSettings.IsChecked = true; break;
            case "diagnostics": NavDiagnostics.IsChecked = true; break;
            default: NavDashboard.IsChecked = true; break;
        }
    }

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (DashboardPage is null) return; // during InitializeComponent
        DashboardPage.Visibility = NavDashboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = NavDiagnostics.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _vm.DiagnosticsVisible = NavDiagnostics.IsChecked == true;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ShowVideo)) return;
        if (_vm.ShowVideo && _host.Settings.FullscreenOnConnect && !_fullscreen)
        {
            ShowPage("dashboard");
            ToggleFullscreen();
        }
        else if (!_vm.ShowVideo && _fullscreen)
        {
            ToggleFullscreen();
        }
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            if (!_vm.ShowVideo) return;
            ShowPage("dashboard");
            _restoreState = WindowState;
            _restoreStyle = WindowStyle;
            _restoreResize = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; // re-maximise to cover the taskbar
            WindowState = WindowState.Maximized;
            NavColumn.Width = new GridLength(0);
            NavPane.Visibility = Visibility.Collapsed;
            DashboardPage.SetFullscreen(true);
            _fullscreen = true;
            Activate();
            SetFullscreenHotkeys(true);
        }
        else
        {
            _fullscreen = false;
            SetFullscreenHotkeys(false);
            DashboardPage.SetFullscreen(false);
            NavPane.Visibility = Visibility.Visible;
            NavColumn.Width = new GridLength(220);
            WindowStyle = _restoreStyle;
            ResizeMode = _restoreResize;
            WindowState = _restoreState == WindowState.Minimized ? WindowState.Normal : _restoreState;
        }
    }

    /// <summary>
    /// Esc and F11 as system hotkeys while in full screen: they work even if another window (e.g. the
    /// receiver's video window) has the keyboard focus. Only registered while DXMirror is in full screen
    /// and in front, so other programs keep their Esc/F11.
    /// </summary>
    private void SetFullscreenHotkeys(bool on)
    {
        var hwnd = _source?.Handle ?? 0;
        if (hwnd == 0 || on == _hotkeysRegistered) return;
        if (on)
        {
            NativeMethods.RegisterHotKey(hwnd, HotkeyEscape, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_ESCAPE);
            NativeMethods.RegisterHotKey(hwnd, HotkeyF11, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F11);
        }
        else
        {
            NativeMethods.UnregisterHotKey(hwnd, HotkeyEscape);
            NativeMethods.UnregisterHotKey(hwnd, HotkeyF11);
        }
        _hotkeysRegistered = on;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_fullscreen) return;
        // keep the hotkeys while the video window is in front; release them for any other program
        var fg = NativeMethods.GetForegroundWindow();
        var video = DashboardPage.VideoWindowHandle;
        if (fg != 0 && fg == video) return;
        SetFullscreenHotkeys(false);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && (wParam == HotkeyEscape || wParam == HotkeyF11))
        {
            if (_fullscreen) ToggleFullscreen();
            handled = true;
        }
        return 0;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _fullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // Minimising never stops the receiver; optionally hide to tray.
        if (WindowState == WindowState.Minimized && _host.Settings.MinimizeToTray && !_fullscreen) Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            if (_fullscreen) ToggleFullscreen();
            CloseRequested?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    public void CloseForExit()
    {
        SetFullscreenHotkeys(false);
        _allowClose = true;
        Close();
    }
}
