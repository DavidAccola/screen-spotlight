using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpotlightOverlay.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _toolbarToggleItem;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public event EventHandler? ToggleSpotlightRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ToolbarVisibilityToggleRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(Icon icon)
    {
        _toggleItem = new ToolStripMenuItem("Enable Screen Spotlight");
        _toggleItem.Click += (s, e) => ToggleSpotlightRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _toolbarToggleItem = new ToolStripMenuItem("Hide Toolbar");
        _toolbarToggleItem.Click += (s, e) => ToolbarVisibilityToggleRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(_toggleItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(_toolbarToggleItem);
        contextMenu.Items.Add(exitItem);

        // Fix: WinForms NotifyIcon context menus don't dismiss properly unless the
        // app has foreground focus when the menu opens. SetForegroundWindow on the
        // menu's handle before it shows resolves the "menu stays open" bug.
        contextMenu.Opening += (s, e) =>
        {
            if (contextMenu.Handle != IntPtr.Zero)
                SetForegroundWindow(contextMenu.Handle);
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Screen Spotlight",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.Click += (s, e) =>
        {
            // Only open settings on left-click (not right-click which opens context menu)
            if (e is System.Windows.Forms.MouseEventArgs me && me.Button == MouseButtons.Left)
                SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetEnabled(bool isEnabled)
    {
        _toggleItem.Text = isEnabled ? "Disable Screen Spotlight" : "Enable Screen Spotlight";
    }

    public void SetToolbarVisible(bool visible)
    {
        _toolbarToggleItem.Text = visible ? "Hide Toolbar" : "Show Toolbar";
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
