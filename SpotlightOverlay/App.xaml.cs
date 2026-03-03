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
    private bool _isDismissed; // true when overlay is hidden but cutouts are preserved

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers for debugging silent crashes
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            DebugLog.Write($"[App] UNHANDLED EXCEPTION: {args.ExceptionObject}");
        };
        DispatcherUnhandledException += (s, args) =>
        {
            DebugLog.Write($"[App] DISPATCHER EXCEPTION: {args.Exception}");
            args.Handled = true;
        };

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
        _inputHook.DragStyle = _settings.DragStyle;
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
        _inputHook.RestoreRequested += OnRestoreRequested;

        // Keep hook in sync when settings change at runtime
        _settings.SettingsChanged += (_, _) =>
        {
            _inputHook.DragStyle = _settings.DragStyle;
        };

        _trayIcon.ShowBalloon("Spotlight Overlay", "Ready — Ctrl+Click to create cutouts");
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
    /// <summary>
    /// Converts a screen-coordinate rect (physical pixels from the hook) to
    /// window-relative DIP coordinates using WPF's built-in PointFromScreen.
    /// </summary>
    private System.Windows.Rect ScreenRectToWindowDip(System.Windows.Rect screenRect)
    {
        var topLeft = _overlayWindow!.PointFromScreen(new System.Windows.Point(screenRect.X, screenRect.Y));
        var bottomRight = _overlayWindow.PointFromScreen(new System.Windows.Point(screenRect.Right, screenRect.Bottom));
        return new System.Windows.Rect(topLeft, bottomRight);
    }

    private int _dragUpdateCount;

    private void OnDragUpdated(object? sender, DragRectEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _dragUpdateCount++;
                if (_dragUpdateCount <= 3 || _dragUpdateCount % 50 == 0)
                    DebugLog.Write($"[App] DragUpdated #{_dragUpdateCount}: screen={e.ScreenRect}, dismissed={_isDismissed}, hasWindow={_overlayWindow != null}");

                // If dismissed and starting a new drag, truly clear everything and start fresh
                if (_isDismissed && _overlayWindow != null)
                {
                    DebugLog.Write("[App] New drag while dismissed — clearing old cutouts");
                    _overlayWindow.Close();
                    _overlayWindow = null;
                    _renderer.ClearCutouts();
                    _pendingCutouts.Clear();
                    _isDismissed = false;
                    _inputHook.CanRestore = false;
                }

                // Create overlay on first move if it doesn't exist yet
                if (_overlayWindow == null)
                {
                    var monitorBounds = MonitorHelper.GetMonitorBounds(e.DragStartPoint);
                    var monitorBoundsDip = MonitorHelper.GetMonitorBoundsDip(e.DragStartPoint);
                    DebugLog.Write($"[App] Creating overlay window: physical={monitorBounds}, dip={monitorBoundsDip}");

                    // Capture screenshot at physical pixel resolution before showing overlay
                    System.Windows.Media.Imaging.BitmapSource? frozenScreenshot = null;
                    if (_settings.FreezeScreen)
                    {
                        frozenScreenshot = Helpers.ScreenCapture.CaptureMonitor(monitorBounds);
                        DebugLog.Write("[App] Screen captured for freeze mode");
                    }

                    // Use DIP bounds for the window so it maps exactly to the monitor
                    var win = new OverlayWindow(monitorBoundsDip, _settings.OverlayOpacity, _settings.FeatherRadius);
                    try
                    {
                        win.Show();
                    }
                    catch (System.ComponentModel.Win32Exception w32ex)
                    {
                        DebugLog.Write($"[App] Window.Show() failed during preview: {w32ex.Message}");
                        return;
                    }

                    if (frozenScreenshot != null)
                        win.SetFrozenBackground(frozenScreenshot);

                    _overlayWindow = win;
                    DebugLog.Write("[App] Overlay window created and shown");
                }

                var windowRect = ScreenRectToWindowDip(e.ScreenRect);
                _overlayWindow.ShowDragPreview(windowRect, _settings.PreviewStyle);
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
        _dragUpdateCount = 0;
        Dispatcher.BeginInvoke(() =>
        {
          try
          {
            if (_overlayWindow == null) return;

            var windowRect = ScreenRectToWindowDip(e.ScreenRect);

            DebugLog.Write($"[App] Queuing pending cutout: {windowRect}");
            _pendingCutouts.Add(windowRect);
            _overlayWindow.FinalizeDragPreview(windowRect, _settings.PreviewStyle);
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
            var featheredMask = _renderer.BuildFeatheredMask(overlaySize);
            _overlayWindow.ApplyFeatheredMask(featheredMask);

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
        Dispatcher.BeginInvoke(() =>
        {
            if (_overlayWindow == null || _isDismissed)
            {
                DebugLog.Write("[App] No overlay to dismiss or already dismissed");
                return;
            }

            DebugLog.Write("[App] Dismissing overlay");
            _isDismissed = true;
            _inputHook.CanRestore = true;
            _overlayWindow.BeginFadeOut(() =>
            {
                DebugLog.Write("[App] Fade-out complete, overlay hidden (cutouts preserved)");
            });
        });
    }

    private void OnRestoreRequested(object? sender, EventArgs e)
    {
        DebugLog.Write("[App] OnRestoreRequested received");
        Dispatcher.BeginInvoke(() =>
        {
            if (_overlayWindow == null || !_isDismissed)
            {
                DebugLog.Write("[App] Nothing to restore");
                return;
            }

            DebugLog.Write("[App] Restoring overlay");
            _isDismissed = false;
            _inputHook.CanRestore = false;
            _overlayWindow.BeginFadeIn();
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
