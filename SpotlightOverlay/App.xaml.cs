using System.Diagnostics;
using System.Drawing;
using System.Windows;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Input;
using SpotlightOverlay.Models;
using SpotlightOverlay.Rendering;
using SpotlightOverlay.Services;
using SpotlightOverlay.Windows;

namespace SpotlightOverlay;

public partial class App : Application
{
    private SettingsService _settings = null!;
    private SpotlightRenderer _renderer = null!;
    private GlobalInputHook _inputHook = null!;
    private TrayIconService _trayIcon = null!;
    private OverlayWindow? _overlayWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize services
        _settings = new SettingsService();
        _settings.Load();

        _renderer = new SpotlightRenderer(_settings);

        _inputHook = new GlobalInputHook();

        _trayIcon = new TrayIconService(LoadApplicationIcon());

        // Wire hook error reporting to tray balloon notifications (Req 2.5)
        _inputHook.OnError = error => _trayIcon.ShowBalloon("Spotlight Overlay", error);

        // Install hooks and enable by default
        _inputHook.Install();
        _inputHook.IsEnabled = true;
        _trayIcon.SetEnabled(true);
        DebugLog.Write($"[App] Startup complete. Hook IsEnabled: {_inputHook.IsEnabled}");

        // Wire tray icon events
        _trayIcon.ToggleSpotlightRequested += OnToggleSpotlight;
        _trayIcon.SettingsRequested += OnSettingsRequested;
        _trayIcon.ExitRequested += OnExitRequested;

        // Wire input hook events
        _inputHook.DragCompleted += OnDragCompleted;
        _inputHook.DismissRequested += OnDismissRequested;
    }

    /// <summary>
    /// Toggles the global input hook between active and inactive states (Req 1.4).
    /// </summary>
    private void OnToggleSpotlight(object? sender, EventArgs e)
    {
        _inputHook.IsEnabled = !_inputHook.IsEnabled;
        _trayIcon.SetEnabled(_inputHook.IsEnabled);
    }

    /// <summary>
    /// Opens or focuses the settings window (Req 9.1, 9.4).
    /// </summary>
    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        SettingsWindow.ShowSingleton(_settings);
    }

    /// <summary>
    /// Cleans up hooks, disposes tray icon, and shuts down the application (Req 1.3).
    /// </summary>
    private void OnExitRequested(object? sender, EventArgs e)
    {
        _inputHook.Dispose();
        _trayIcon.Dispose();
        Shutdown();
    }

    /// <summary>
    /// Handles a completed drag gesture: identifies the target monitor, creates or reuses
    /// the overlay window, translates coordinates, adds a cutout, and applies the mask
    /// (Req 2.3, 3.1, 7.1, 7.3).
    /// </summary>
    private void OnDragCompleted(object? sender, DragRectEventArgs e)
    {
        DebugLog.Write($"[App] OnDragCompleted received: ScreenRect={e.ScreenRect}, DragStart={e.DragStartPoint}");
        // Hook callbacks fire on a non-UI thread — dispatch to WPF dispatcher (async to avoid blocking hooks)
        Dispatcher.BeginInvoke(() =>
        {
          try
          {
            DebugLog.Write("[App] Inside Dispatcher.BeginInvoke for DragCompleted");

            var monitorBounds = MonitorHelper.GetMonitorBounds(e.DragStartPoint);
            var monitorTopLeft = new System.Windows.Point(monitorBounds.X, monitorBounds.Y);
            DebugLog.Write($"[App] Screen rect: {e.ScreenRect}, Monitor: {monitorBounds}");

            // Create overlay on first drag, reuse for subsequent drags (Req 3.1)
            if (_overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow(monitorBounds, _settings.OverlayOpacity);
                _overlayWindow.Show();
                _overlayWindow.SetClickThrough(true);
                DebugLog.Write($"[App] OverlayWindow shown: L={_overlayWindow.Left} T={_overlayWindow.Top} W={_overlayWindow.ActualWidth} H={_overlayWindow.ActualHeight}");
            }

            // DPI: hook gives physical pixels, WPF uses DIPs. Scale using actual window size.
            double actualW = _overlayWindow.ActualWidth;
            double actualH = _overlayWindow.ActualHeight;
            double dpiScaleX = actualW > 0 ? monitorBounds.Width / actualW : 1.0;
            double dpiScaleY = actualH > 0 ? monitorBounds.Height / actualH : 1.0;
            DebugLog.Write($"[App] DPI scale: X={dpiScaleX}, Y={dpiScaleY}");

            // Translate screen coordinates to window-relative DIP coordinates
            var screenRect = e.ScreenRect;
            var windowRect = new System.Windows.Rect(
                (screenRect.X - monitorTopLeft.X) / dpiScaleX,
                (screenRect.Y - monitorTopLeft.Y) / dpiScaleY,
                screenRect.Width / dpiScaleX,
                screenRect.Height / dpiScaleY);
            DebugLog.Write($"[App] Window-relative rect (DIP): {windowRect}, Feather: {_settings.FeatherRadius}");

            // Add cutout and rebuild clip geometry (Req 4.1, 4.3)
            _renderer.AddCutout(windowRect);
            var overlaySize = new System.Windows.Size(_overlayWindow.ActualWidth, _overlayWindow.ActualHeight);
            var clipGeometry = _renderer.BuildClipGeometry(overlaySize);
            DebugLog.Write($"[App] Clip built: {_renderer.CutoutCount} cutouts");
            _overlayWindow.ApplyClipGeometry(clipGeometry);
            DebugLog.Write("[App] Clip applied");

            _overlayWindow.SetClickThrough(true);
          }
          catch (Exception ex)
          {
            DebugLog.Write($"[App] ERROR in DragCompleted: {ex}");
          }
        });
    }

    private void OnDismissRequested(object? sender, EventArgs e)
    {
        DebugLog.Write("[App] OnDismissRequested received");
        // Hook callbacks fire on a non-UI thread — dispatch to WPF dispatcher (async to avoid blocking hooks)
        Dispatcher.BeginInvoke(() =>
        {
            if (_overlayWindow == null)
            {
                DebugLog.Write("[App] No overlay window to dismiss");
                return;
            }

            DebugLog.Write("[App] Beginning fade-out");
            var window = _overlayWindow;
            _overlayWindow = null;

            window.BeginFadeOut(() =>
            {
                _renderer.ClearCutouts();
                DebugLog.Write("[App] Fade-out complete, cutouts cleared");
            });
        });
    }
    /// <summary>
    /// Loads the application icon from the embedded assembly resource.
    /// Falls back to SystemIcons.Application if no embedded icon is found.
    /// </summary>
    private static Icon LoadApplicationIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var iconStream = assembly.GetManifestResourceStream(
                assembly.GetName().Name + ".app.ico");
            if (iconStream != null)
            {
                return new Icon(iconStream);
            }
        }
        catch
        {
            // Fall through to default icon
        }

        return SystemIcons.Application;
    }
}
