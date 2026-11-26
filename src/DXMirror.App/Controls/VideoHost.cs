using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DXMirror.App.Platform;

namespace DXMirror.App.Controls;

/// <summary>
/// Placeholder in the WPF layout where the receiver's video appears.
///
/// The receiver (uxplay.exe) is a separate process and renders with Direct3D into its own window.
/// Tests showed: rendering into an DXMirror window (GstVideoOverlay) and re-parenting the receiver
/// window as a child both leave the area grey. So the receiver window stays a real top-level window,
/// DXMirror removes its frame, makes it an *owned* window of the DXMirror main window (stays above
/// it, minimises with it) and keeps it exactly on top of this placeholder.
/// </summary>
public sealed class VideoHost : HwndHost
{
    private nint _video;
    private bool _shown;
    private (int X, int Y, int W, int H) _lastBounds;

    public nint VideoHandle { get; private set; }

    public bool HasVideoWindow => _video != 0 && NativeMethods.IsWindow(_video);

    /// <summary>The receiver's video window while attached, otherwise 0.</summary>
    public nint VideoWindow => HasVideoWindow ? _video : 0;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var hwnd = NativeMethods.CreateWindowEx(
            0, "static", "DXMirrorVideo",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS
            | NativeMethods.SS_BLACKRECT,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, 0, 0, 0);
        if (hwnd == 0) throw new InvalidOperationException("Could not create video window (error " + Marshal.GetLastWin32Error() + ")");
        VideoHandle = hwnd;
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        HideVideo();
        _video = 0;
        NativeMethods.DestroyWindow(hwnd.Handle);
        VideoHandle = 0;
    }

    /// <summary>Finds the receiver's video window (visible top-level window of the process) and attaches it.</summary>
    public bool TryAttach(int processId)
    {
        if (VideoHandle == 0) return false;
        if (HasVideoWindow) return true;
        _video = 0;
        _shown = false;
        _lastBounds = default;

        nint found = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)processId || !NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.GetParent(hwnd) != 0) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var r) || r.Width < 16 || r.Height < 16) return true;
            found = hwnd;
            return false;
        }, 0);
        if (found == 0) return false;

        // borderless popup without taskbar button
        var style = (long)NativeMethods.GetWindowLongPtr(found, NativeMethods.GWL_STYLE);
        style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | NativeMethods.WS_SYSMENU
                   | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX);
        style |= NativeMethods.WS_POPUP;
        NativeMethods.SetWindowLongPtr(found, NativeMethods.GWL_STYLE, (nint)style);
        var ex = (long)NativeMethods.GetWindowLongPtr(found, NativeMethods.GWL_EXSTYLE);
        ex &= ~(NativeMethods.WS_EX_APPWINDOW | NativeMethods.WS_EX_DLGMODALFRAME
                | NativeMethods.WS_EX_WINDOWEDGE | NativeMethods.WS_EX_CLIENTEDGE);
        // a click into the video must not take the keyboard away from DXMirror (Esc/F11 in full screen),
        // and UxPlay's own Alt+Enter full screen must not be reachable
        ex |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(found, NativeMethods.GWL_EXSTYLE, (nint)ex);

        // owned by the DXMirror main window: always above it, minimised/restored with it
        var root = NativeMethods.GetAncestor(VideoHandle, NativeMethods.GA_ROOT);
        if (root != 0) NativeMethods.SetWindowLongPtr(found, NativeMethods.GWLP_HWNDPARENT, root);

        _video = found;
        Sync();
        return true;
    }

    /// <summary>Moves the video window onto the placeholder, or hides it when the placeholder is not visible.</summary>
    public void Sync()
    {
        if (!HasVideoWindow)
        {
            _video = 0;
            return;
        }
        var root = NativeMethods.GetAncestor(VideoHandle, NativeMethods.GA_ROOT);
        bool visible = IsVisible && VideoHandle != 0 && NativeMethods.IsWindowVisible(VideoHandle)
                       && root != 0 && NativeMethods.IsWindowVisible(root) && !NativeMethods.IsIconic(root);
        if (!visible)
        {
            HideVideo();
            return;
        }
        NativeMethods.GetWindowRect(VideoHandle, out var r);   // screen coordinates, physical pixels
        var bounds = (r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height));
        if (_shown && bounds == _lastBounds) return;
        _lastBounds = bounds;
        _shown = true;
        NativeMethods.SetWindowPos(_video, 0, bounds.Left, bounds.Top, bounds.Item3, bounds.Item4,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
    }

    private void HideVideo()
    {
        _shown = false;
        if (HasVideoWindow && NativeMethods.IsWindowVisible(_video))
            NativeMethods.ShowWindow(_video, NativeMethods.SW_HIDE);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        Sync();
    }
}
