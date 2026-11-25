using System.Drawing;
using WinForms = System.Windows.Forms;
using DXMirror.App.Infrastructure;

namespace DXMirror.App.Services;

/// <summary>System-tray icon with context menu and balloon/toast notifications.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _open, _start, _stop, _disconnect, _status, _settings, _exit;

    public event Action? OpenRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? DisconnectRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _open = new WinForms.ToolStripMenuItem("", null, (_, _) => OpenRequested?.Invoke()) { Font = new Font(WinForms.Control.DefaultFont, FontStyle.Bold) };
        _status = new WinForms.ToolStripMenuItem("") { Enabled = false };
        _start = new WinForms.ToolStripMenuItem("", null, (_, _) => StartRequested?.Invoke());
        _stop = new WinForms.ToolStripMenuItem("", null, (_, _) => StopRequested?.Invoke());
        _disconnect = new WinForms.ToolStripMenuItem("", null, (_, _) => DisconnectRequested?.Invoke());
        _settings = new WinForms.ToolStripMenuItem("", null, (_, _) => SettingsRequested?.Invoke());
        _exit = new WinForms.ToolStripMenuItem("", null, (_, _) => ExitRequested?.Invoke());

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _open, _status, new WinForms.ToolStripSeparator(), _start, _stop, _disconnect,
            new WinForms.ToolStripSeparator(), _settings, new WinForms.ToolStripSeparator(), _exit,
        });

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu,
            Text = "DXMirror",
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        UpdateTexts();
    }

    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DXMirror.ico");
        try { return File.Exists(path) ? new Icon(path) : SystemIcons.Application; }
        catch (ArgumentException) { return SystemIcons.Application; }
    }

    public void UpdateTexts()
    {
        _open.Text = Loc.Get("Tray.Open");
        _start.Text = Loc.Get("Tray.Start");
        _stop.Text = Loc.Get("Tray.Stop");
        _disconnect.Text = Loc.Get("Tray.Disconnect");
        _settings.Text = Loc.Get("Tray.Settings");
        _exit.Text = Loc.Get("Tray.Exit");
    }

    public void UpdateState(bool running, bool hasClient, string statusText)
    {
        _start.Enabled = !running;
        _stop.Enabled = running;
        _disconnect.Enabled = hasClient;
        _status.Text = statusText;
        // NotifyIcon.Text is limited to 127 characters
        var tip = "DXMirror – " + statusText;
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    public void Notify(string title, string text)
    {
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
