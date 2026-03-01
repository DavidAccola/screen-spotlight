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
    private readonly List<System.Windows.Rect> _pendingCutouts = new();

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
        _inputHook.DragUpdated += OnDragUpdated;
        _inputHook.DragCancelled += OnDragCancelled;
        _inputHook.CtrlReleased += OnCtrlReleased;
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
    /// Handles drag cancellation (e.g., right-click during drag) — hides the preview.
    /// </summary>
    private void OnDragCancelled(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _overlayWindow?.HideDragPreview();
        });
    }

    /// <summary>
    /// Handles live drag updates: shows a preview rectangle on the overlay as the user drags.
    /// </summary>
    private void OnDragUpdated(object? sender, DragRectEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var monitorBounds = MonitorHelper.GetMonitorBounds(e.DragStartPoint);
                var monitorTopLeft = new System.Windows.Point(monitorBounds.X, monitorBounds.Y);

                // Create overlay on first move if it doesn't exist yet
                if (_overlayWindow == null)
                {
                    var win = new OverlayWindow(monitorBounds, _settings.OverlayOpacity);
                    try
                    {
                        win.Show();
                    }
                    catch (System.ComponentModel.Win32Exception w32ex)
                    {
                        DebugLog.Write($"[App] Window.Show() failed during preview: {w32ex.Message}");
                        return;
                    }
                    win.SetClickThrough(true);
                    _overlayWindow = win;
                }

                double actualW = _overlayWindow.ActualWidth;
                double actualH = _overlayWindow.ActualHeight;
                double dpiScaleX = actualW > 0 ? monitorBounds.Width / actualW : 1.0;
                double dpiScaleY = actualH > 0 ? monitorBounds.Height / actualH : 1.0;

                var screenRect = e.ScreenRect;
                var windowRect = new System.Windows.Rect(
                    (screenRect.X - monitorTopLeft.X) / dpiScaleX,
                    (screenRect.Y - monitorTopLeft.Y) / dpiScaleY,
                    screenRect.Width / dpiScaleX,
                    screenRect.Height / dpiScaleY);

                _overlayWindow.ShowDragPreview(windowRect);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in DragUpdated: {ex}");
            }
        });
    }

    /// <summary>
    /// Handles a completed drag gesture: identifies the target monitor, creates or reuses
    /// the overlay window, translates coordinates, adds a cutout, and applies the mask
    /// <summary>
    /// Handles a completed drag gesture: queues the cutout rect and finalizes the preview box.
    /// The actual cutout is applied when Ctrl is released (OnCtrlReleased).
    /// </summary>
    private void OnDragCompleted(object? sender, DragRectEventArgs e)
    {
        DebugLog.Write($"[App] OnDragCompleted received: ScreenRect={e.ScreenRect}, DragStart={e.DragStartPoint}");
        Dispatcher.BeginInvoke(() =>
        {
          try
          {
            if (_overlayWindow == null) return;

            var monitorBounds = MonitorHelper.GetMonitorBounds(e.DragStartPoint);
            var monitorTopLeft = new System.Windows.Point(monitorBounds.X, monitorBounds.Y);

            double actualW = _overlayWindow.ActualWidth;
            double actualH = _overlayWindow.ActualHeight;
            double dpiScaleX = actualW > 0 ? monitorBounds.Width / actualW : 1.0;
            double dpiScaleY = actualH > 0 ? monitorBounds.Height / actualH : 1.0;

            var screenRect = e.ScreenRect;
            var windowRect = new System.Windows.Rect(
                (screenRect.X - monitorTopLeft.X) / dpiScaleX,
                (screenRect.Y - monitorTopLeft.Y) / dpiScaleY,
                screenRect.Width / dpiScaleX,
                screenRect.Height / dpiScaleY);

            DebugLog.Write($"[App] Queuing pending cutout: {windowRect}");
            _pendingCutouts.Add(windowRect);
            _overlayWindow.FinalizeDragPreview(windowRect);
          }
          catch (Exception ex)
          {
            DebugLog.Write($"[App] ERROR in DragCompleted: {ex}");
          }
        });
    }

    /// <summary>
    /// Handles Ctrl key release: applies all pending cutouts at once with fade-in.
    /// </summary>
    private void OnCtrlReleased(object? sender, EventArgs e)
    {
        DebugLog.Write($"[App] OnCtrlReleased, pending cutouts: {_pendingCutouts.Count}");
        Dispatcher.BeginInvoke(() =>
        {
          try
          {
            if (_overlayWindow == null || _pendingCutouts.Count == 0) return;

            // Apply all pending cutouts
            foreach (var rect in _pendingCutouts)
            {
                _renderer.AddCutout(rect);
            }

            var overlaySize = new System.Windows.Size(_overlayWindow.ActualWidth, _overlayWindow.ActualHeight);
            var clipGeometry = _renderer.BuildClipGeometry(overlaySize);
            _overlayWindow.ApplyClipGeometry(clipGeometry);

            // Fade in background on first batch, animate individual cutouts on subsequent batches
            bool isFirstBatch = _overlayWindow.FadeInBackground();
            if (!isFirstBatch)
            {
                foreach (var rect in _pendingCutouts)
                {
                    _overlayWindow.AnimateCutoutFadeIn(rect);
                }
            }

            // Clear finalized preview boxes and pending list
            _overlayWindow.ClearFinalizedPreviews();
            _pendingCutouts.Clear();

            _overlayWindow.SetClickThrough(true);
            DebugLog.Write($"[App] Applied {_renderer.CutoutCount} total cutouts");
          }
          catch (Exception ex)
          {
            DebugLog.Write($"[App] ERROR in CtrlReleased: {ex}");
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
                _pendingCutouts.Clear();
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
