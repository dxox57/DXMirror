using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using DXMirror.Core.Receiver;
using DXMirror.App.ViewModels;

namespace DXMirror.App.Views;

public partial class DashboardView : UserControl
{
    private MainViewModel? _vm;
    private bool _fullscreen;

    private readonly DispatcherTimer _adoptTimer;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        // GStreamer opens the receiver's video window when mirroring starts (and a new one per session):
        // look for it a few times per second while an iPhone is connected and embed it.
        _adoptTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _adoptTimer.Tick += (_, _) => TryAdoptVideoWindow();
        _adoptTimer.Start();

        // keep the video window glued to the placeholder when the main window moves / resizes / hides
        IsVisibleChanged += (_, _) => Video.Sync();
        Loaded += (_, _) =>
        {
            var w = Window.GetWindow(this);
            if (w is null) return;
            w.LocationChanged += (_, _) => Video.Sync();
            w.SizeChanged += (_, _) => Video.Sync();
            w.StateChanged += (_, _) => Video.Sync();
            w.IsVisibleChanged += (_, _) => Video.Sync();
        };
    }

    private void TryAdoptVideoWindow()
    {
        if (_vm is null || !_vm.EmbedVideoEnabled) return;
        if (_vm.State is (ReceiverState.Connecting or ReceiverState.Streaming) && _vm.ReceiverProcessId is { } pid)
            Video.TryAttach(pid);
        Video.Sync();
    }

    public nint VideoHandle => Video.VideoHandle;
    public nint VideoWindowHandle => Video.VideoWindow;

    private void Attach(MainViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        Refresh();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowVideo) or nameof(MainViewModel.Tone) or nameof(MainViewModel.IsRunning)
            or nameof(MainViewModel.State))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_vm is null) return;
        Video.Visibility = _vm.ShowVideo ? Visibility.Visible : Visibility.Hidden;
        Video.Sync();
        IdlePanel.Visibility = _vm.ShowVideo ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Visibility = _vm.IsRunning ? Visibility.Collapsed : Visibility.Visible;

        var (fg, bg) = _vm.Tone switch
        {
            StatusTone.Ok => ("Brush.Success", "Brush.SuccessSubtle"),
            StatusTone.Busy => ("Brush.Accent", "Brush.AccentSubtle"),
            StatusTone.Warning => ("Brush.Warning", "Brush.WarningSubtle"),
            StatusTone.Error => ("Brush.Danger", "Brush.DangerSubtle"),
            _ => ("Brush.TextSecondary", "Brush.SurfaceAlt"),
        };
        StatusPill.SetResourceReference(Border.BackgroundProperty, bg);
        StatusDot.SetResourceReference(Shape.FillProperty, fg);
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, fg);
    }

    /// <summary>Full screen: only the video surface remains, edge to edge.</summary>
    public void SetFullscreen(bool on)
    {
        _fullscreen = on;
        var vis = on ? Visibility.Collapsed : Visibility.Visible;
        Header.Visibility = vis;
        LiveControls.Visibility = vis;
        Cards.Visibility = vis;
        Root.Margin = on ? new Thickness(0) : new Thickness(32, 24, 32, 24);
        VideoFrame.CornerRadius = on ? new CornerRadius(0) : (CornerRadius)FindResource("Radius.Card");
        VideoFrame.BorderThickness = on ? new Thickness(0) : new Thickness(1);
    }

    public bool IsFullscreen => _fullscreen;
}
