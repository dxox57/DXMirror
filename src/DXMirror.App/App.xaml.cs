using System.Windows;
using System.Windows.Threading;
using DXMirror.App.Infrastructure;
using DXMirror.App.Services;
using DXMirror.App.ViewModels;

namespace DXMirror.App;

public partial class App : Application
{
    private const string InstanceId = "DXMirror-7c1e5c1a";
    private SingleInstance? _instance;
    private AppHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Uninstaller hook: "DXMirror.exe --cleanup [--remove-data]"
        if (e.Args.Contains("--cleanup"))
        {
            AppHost.CleanupUserData(e.Args.Contains("--remove-data"));
            Shutdown(0);
            return;
        }

        // Installer hook (runs as the original, non-elevated user): "DXMirror.exe --set-autostart"
        if (e.Args.Contains("--set-autostart"))
        {
            try
            {
                AutostartService.SetEnabled(true);
                var store = new Core.Settings.SettingsStore(AppPaths.SettingsFile, Platform.NativeMethods.ProtectString, Platform.NativeMethods.UnprotectString);
                var s = store.Load();
                s.StartWithWindows = true;
                store.Save(s);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Shutdown(0);
            return;
        }

        _instance = new SingleInstance(InstanceId);
        if (!_instance.IsFirstInstance)
        {
            SingleInstance.SignalFirstInstance(InstanceId);
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _host?.Log.Error("App", "Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _host?.Log.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        _host = new AppHost();
        ThemeManager.Apply(_host.Settings.Theme);
        Loc.Apply(_host.Settings.Language);

        _vm = new MainViewModel(_host, Dispatcher);
        _window = new MainWindow(_vm, _host);
        // The receiver renders into its own window, which DashboardView embeds (see VideoHost).
        _host.SetVideoHandleProvider(() => 0);

        _tray = new TrayIcon();
        _tray.OpenRequested += () => Dispatcher.InvokeAsync(ShowMainWindow);
        _tray.StartRequested += () => Dispatcher.InvokeAsync(() => _vm.StartCommand.Execute(null));
        _tray.StopRequested += () => Dispatcher.InvokeAsync(() => _vm.StopCommand.Execute(null));
        _tray.DisconnectRequested += () => Dispatcher.InvokeAsync(() => _vm.DisconnectCommand.Execute(null));
        _tray.SettingsRequested += () => Dispatcher.InvokeAsync(() => { ShowMainWindow(); _window.ShowPage("settings"); });
        _tray.ExitRequested += () => Dispatcher.InvokeAsync(() => _ = ExitAsync());
        _vm.StateChanged += () => _tray.UpdateState(_vm.IsRunning, _vm.HasClient, _vm.StatusSummary);
        _vm.NotificationRequested += (title, text) => _tray.Notify(title, text);
        Loc.LanguageChanged += () => _tray.UpdateTexts();
        _instance.ActivationRequested += () => Dispatcher.InvokeAsync(ShowMainWindow);

        _window.CloseRequested += () =>
        {
            if (_exiting) return;
            if (_host.Settings.CloseToTray) _window.Hide();
            else _ = ExitAsync();
        };

        // The window is always created (the video surface needs an HWND), then hidden for autostart.
        bool startHidden = e.Args.Contains(AutostartService.MinimizedArgument) && _host.Settings.MinimizeToTray;
        _window.Show();
        if (startHidden) _window.Hide();

        if (_host.Settings.StartReceiverOnLaunch)
        {
            // let the video window get its handle first
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            try { await _vm.StartAsync(); }
            catch (Exception ex) { _host.Log.Error("App", "Receiver start failed", ex); }
        }
    }

    private void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    internal async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _tray?.Dispose();
        _tray = null;
        try
        {
            if (_host is not null) await _host.DisposeAsync();
        }
        catch (Exception) { /* shutting down anyway */ }
        _window?.CloseForExit();
        _instance?.Dispose();
        Shutdown(0);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Log.Error("UI", "Unhandled UI exception", e.Exception);
        // keep the receiver and the app alive; a UI glitch must not end a running mirroring session
        e.Handled = true;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        _host?.Receiver.StopAsync().Wait(TimeSpan.FromSeconds(3));
    }
}
