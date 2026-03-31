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
    private static Mutex? _singleInstanceMutex;
    private SettingsService _settings = null!;
    private SpotlightRenderer _renderer = null!;
    private readonly ArrowRenderer _arrowRenderer = new();
    private GlobalInputHook _inputHook = null!;
    private TrayIconService _trayIcon = null!;
    private FlyoutToolbarWindow? _flyoutToolbar;
    private OverlayWindow? _overlayWindow;
    private readonly List<System.Windows.Rect> _pendingCutouts = new();
    private bool _isDismissed; // true when overlay is hidden but cutouts are preserved
    private System.Windows.Media.Imaging.BitmapSource? _cachedScreenshot; // pre-captured on Ctrl press

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance check — exit silently if another instance is already running
        _singleInstanceMutex = new Mutex(true, "SpotlightOverlay_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

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
        _inputHook.ActivationModifier = _settings.ActivationModifier;
        _inputHook.ActivationKey = _settings.ActivationKey;
        _inputHook.ToggleModifier = _settings.ToggleModifier;
        _inputHook.ToggleKey = _settings.ToggleKey;
        _trayIcon.SetEnabled(true);
        DebugLog.Write($"[App] Startup complete. Hook IsEnabled: {_inputHook.IsEnabled}");

        // Wire tray icon events
        _trayIcon.ToggleSpotlightRequested += OnToggleSpotlight;
        _trayIcon.SettingsRequested += OnSettingsRequested;
        _trayIcon.ToolbarVisibilityToggleRequested += OnToolbarVisibilityToggle;
        _trayIcon.ExitRequested += OnExitRequested;

        // Wire input hook events
        _inputHook.DragCompleted += OnDragCompleted;
        _inputHook.DragUpdated += OnDragUpdated;
        _inputHook.DragCancelled += OnDragCancelled;
        _inputHook.CtrlReleased += OnCtrlReleased;
        _inputHook.DismissRequested += OnDismissRequested;
        _inputHook.RestoreRequested += OnRestoreRequested;
        _inputHook.CtrlPressed += OnCtrlPressed;
        _inputHook.ToggleRequested += OnToggleSpotlight;
        _inputHook.ArrowDragUpdated += OnArrowDragUpdated;
        _inputHook.ArrowDragCompleted += OnArrowDragCompleted;

        // Keep hook in sync when settings change at runtime
        _settings.SettingsChanged += (_, _) =>
        {
            _inputHook.DragStyle = _settings.DragStyle;
            _inputHook.ActivationModifier = _settings.ActivationModifier;
            _inputHook.ActivationKey = _settings.ActivationKey;
            _inputHook.ToggleModifier = _settings.ToggleModifier;
            _inputHook.ToggleKey = _settings.ToggleKey;
            _trayIcon.SetToolbarVisible(_settings.FlyoutToolbarVisible);
        };

        // Create flyout toolbar (Req 7.2, 9.1, 11.1)
        try
        {
            _flyoutToolbar = new FlyoutToolbarWindow(_settings);
            _flyoutToolbar.ActiveToolChanged += OnActiveToolChanged;
            if (_settings.FlyoutToolbarVisible)
                _flyoutToolbar.ShowToolbar();
            _trayIcon.SetToolbarVisible(_settings.FlyoutToolbarVisible);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[App] Flyout toolbar init failed (non-fatal): {ex.Message}");
            _flyoutToolbar = null;
        }

        FlyoutNotification.Show("Screen Spotlight enabled");

        // Pre-warm WPF window infrastructure at idle priority so the first
        // Ctrl+click doesn't pay the JIT/XAML-parse cost (~250ms).
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            try
            {
                var primaryBounds = MonitorHelper.GetMonitorBoundsDip(new System.Windows.Point(0, 0));
                var warmup = new OverlayWindow(primaryBounds, 0, 0);
                warmup.Show();
                warmup.Close();
                DebugLog.Write("[App] Pre-warm window created and closed");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] Pre-warm failed (non-fatal): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Toggles the global input hook between active and inactive states (Req 1.4).
    /// </summary>
    private void OnToggleSpotlight(object? sender, EventArgs e)
    {
        _inputHook.IsEnabled = !_inputHook.IsEnabled;
        _trayIcon.SetEnabled(_inputHook.IsEnabled);
        FlyoutNotification.Show(_inputHook.IsEnabled
            ? "Screen Spotlight enabled"
            : "Screen Spotlight disabled");
    }

    /// <summary>
    /// Opens or focuses the settings window (Req 9.1, 9.4).
    /// </summary>
    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        SettingsWindow.ShowSingleton(_settings, _inputHook);
    }

    /// <summary>
    /// Cleans up hooks, disposes tray icon, and shuts down the application (Req 1.3).
    /// </summary>
    private void OnExitRequested(object? sender, EventArgs e)
    {
        _flyoutToolbar?.Close();
        _inputHook.Dispose();
        _trayIcon.Dispose();
        Shutdown();
    }

    /// <summary>
    /// Toggles the flyout toolbar visibility from the tray icon menu (Req 9.2, 9.3).
    /// </summary>
    private void OnToolbarVisibilityToggle(object? sender, EventArgs e)
    {
        _settings.FlyoutToolbarVisible = !_settings.FlyoutToolbarVisible;
        _settings.Save();
        if (_settings.FlyoutToolbarVisible)
            _flyoutToolbar?.ShowToolbar();
        else
            _flyoutToolbar?.HideToolbar();
        _trayIcon.SetToolbarVisible(_settings.FlyoutToolbarVisible);
    }

    /// <summary>
    /// Handles drag cancellation (e.g., right-click during drag) — hides the preview.
    /// </summary>
    private void OnDragCancelled(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _overlayWindow?.HideDragPreview();
            _overlayWindow?.HideArrowPreview();
            _cachedScreenshot = null;
        });
    }

    /// <summary>
    /// Handles active tool changes from the flyout toolbar.
    /// </summary>
    private void OnActiveToolChanged(object? sender, ToolType tool)
    {
        _inputHook.ActiveTool = tool;
        DebugLog.Write($"[App] ActiveTool changed to {tool}");
    }

    /// <summary>
    /// Pre-captures a screenshot on Ctrl keydown (before any click) so that
    /// menus/tooltips are still visible in the frozen image.
    /// </summary>
    private void OnCtrlPressed(object? sender, EventArgs e)
    {
        if (!_settings.FreezeScreen || _overlayWindow != null) return;

        // Capture screenshot synchronously on the hook thread — this runs before
        // the message pump processes anything, so transient UI (Start menu, tooltips,
        // popups) is captured while still visible. CaptureMonitor uses GDI BitBlt
        // and returns a Frozen BitmapSource, so it's safe from any thread.
        try
        {
            var cursorPos = System.Windows.Forms.Cursor.Position;
            var screenPoint = new System.Windows.Point(cursorPos.X, cursorPos.Y);
            var monitorBounds = MonitorHelper.GetMonitorBounds(screenPoint);
            _cachedScreenshot = Helpers.ScreenCapture.CaptureMonitor(monitorBounds);
            DebugLog.Write("[App] Pre-captured screenshot on modifier press (sync)");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[App] Pre-capture failed: {ex.Message}");
        }
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

    /// <summary>
    /// Ensures the overlay window exists, creating it if needed (shared by spotlight and arrow flows).
    /// Returns true if the overlay is ready, false if creation failed.
    /// </summary>
    private bool EnsureOverlayWindow(System.Windows.Point screenDragStart)
    {
        if (_overlayWindow != null) return true;

        var monitorBounds = MonitorHelper.GetMonitorBounds(screenDragStart);
        var monitorBoundsDip = MonitorHelper.GetMonitorBoundsDip(screenDragStart);
        DebugLog.Write($"[App] Creating overlay window: physical={monitorBounds}, dip={monitorBoundsDip}");

        System.Windows.Media.Imaging.BitmapSource? frozenScreenshot = null;
        if (_settings.FreezeScreen)
        {
            frozenScreenshot = _cachedScreenshot ?? Helpers.ScreenCapture.CaptureMonitor(monitorBounds);
            _cachedScreenshot = null;
            DebugLog.Write($"[App] Using {(frozenScreenshot == _cachedScreenshot ? "fallback" : "pre-captured")} screenshot for freeze mode");
        }

        var win = new OverlayWindow(monitorBoundsDip, _settings.OverlayOpacity, _settings.FeatherRadius);
        // Tell the overlay to block clicks before Show() so the Loaded handler knows
        if (frozenScreenshot != null)
        {
            win.WillHaveFrozenBackground = true;
        }
        try
        {
            win.Show();
        }
        catch (System.ComponentModel.Win32Exception w32ex)
        {
            DebugLog.Write($"[App] Window.Show() failed during preview: {w32ex.Message}");
            return false;
        }

        if (frozenScreenshot != null)
        {
            win.SetFrozenBackground(frozenScreenshot);
            // Only fade in the dark overlay for spotlight tool — arrows don't darken the screen
            if (_inputHook.ActiveTool == ToolType.Spotlight)
                win.FadeInBackground(300);
            win.ForceTopmost();
            DismissStartMenu(monitorBounds);
        }

        _overlayWindow = win;
        
        // Always re-force toolbar above the overlay after all z-order changes
        ForceToolbarAboveOverlay();
        
        DebugLog.Write("[App] Overlay window created and shown");
        return true;
    }

    /// <summary>
    /// Clears dismissed state and old overlay when starting a new drag while dismissed.
    /// Shared by spotlight and arrow drag flows.
    /// </summary>
    private void ClearDismissedState()
    {
        if (!_isDismissed || _overlayWindow == null) return;

        DebugLog.Write("[App] New drag while dismissed — clearing old state");
        _overlayWindow.Close();
        _overlayWindow = null;
        _renderer.ClearCutouts();
        _arrowRenderer.ClearArrows();
        _pendingCutouts.Clear();
        _isDismissed = false;
        _inputHook.CanRestore = false;
    }

    /// <summary>
    /// Handles live arrow drag updates: shows a preview arrow on the overlay.
    /// </summary>
    private void OnArrowDragUpdated(object? sender, ArrowLineEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ClearDismissedState();

                if (!EnsureOverlayWindow(e.StartPoint)) return;

                var dipStart = _overlayWindow!.PointFromScreen(e.StartPoint);
                var dipEnd = _overlayWindow.PointFromScreen(e.EndPoint);

                var color = ParseArrowColor();
                var leftEnd = _settings.ArrowheadStyle;
                var rightEnd = _settings.ArrowEndStyle;
                var lineStyle = _settings.ArrowLineStyle;

                var previewPath = _arrowRenderer.BuildArrowPath(dipStart, dipEnd, color, leftEnd, rightEnd, lineStyle);
                if (previewPath != null)
                    _overlayWindow.ShowArrowPreview(previewPath);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in ArrowDragUpdated: {ex}");
            }
        });
    }

    /// <summary>
    /// Handles a completed arrow drag: adds the finalized arrow (shadow + main) to the overlay.
    /// </summary>
    private void OnArrowDragCompleted(object? sender, ArrowLineEventArgs e)
    {
        DebugLog.Write($"[App] OnArrowDragCompleted: Start={e.StartPoint}, End={e.EndPoint}");
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_overlayWindow == null) return;

                _overlayWindow.HideArrowPreview();

                var dipStart = _overlayWindow.PointFromScreen(e.StartPoint);
                var dipEnd = _overlayWindow.PointFromScreen(e.EndPoint);

                var color = ParseArrowColor();
                var leftEnd = _settings.ArrowheadStyle;
                var rightEnd = _settings.ArrowEndStyle;
                var lineStyle = _settings.ArrowLineStyle;

                // Add shadow path first (renders behind the main arrow)
                var shadowPath = _arrowRenderer.BuildShadowPath(dipStart, dipEnd, leftEnd, rightEnd, lineStyle);
                if (shadowPath != null)
                    _overlayWindow.AddArrowVisual(shadowPath);

                // Add main arrow path
                var mainPath = _arrowRenderer.BuildArrowPath(dipStart, dipEnd, color, leftEnd, rightEnd, lineStyle);
                if (mainPath != null)
                {
                    _overlayWindow.AddArrowVisual(mainPath);
                    _arrowRenderer.AddArrow(dipStart, dipEnd);
                }

                // Note: do NOT call FadeInBackground() here — the dark overlay
                // is only for spotlights. Arrows render on a transparent overlay.
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in ArrowDragCompleted: {ex}");
            }
        });
    }

    /// <summary>
    /// Parses the ArrowColor hex string from settings into a WPF Color.
    /// Falls back to white if parsing fails.
    /// </summary>
    private System.Windows.Media.Color ParseArrowColor()
    {
        try
        {
            var hex = _settings.ArrowColor;
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return System.Windows.Media.Colors.White;
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

                ClearDismissedState();

                if (!EnsureOverlayWindow(e.DragStartPoint)) return;

                var windowRect = ScreenRectToWindowDip(e.ScreenRect);
                _overlayWindow!.ShowDragPreview(windowRect, _settings.PreviewStyle);
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

            // In replace mode, fade out old cutouts then apply new ones
            if (!_settings.CumulativeSpotlights && _renderer.CutoutCount > 0)
            {
                var oldCutouts = _renderer.Cutouts.ToList();
                var pendingSnapshot = _pendingCutouts.ToList();
                _pendingCutouts.Clear();

                _overlayWindow.AnimateCutoutsFadeOut(oldCutouts, () =>
                {
                    _renderer.ClearCutouts();
                    ApplyCutouts(pendingSnapshot);
                }, durationMs: 200);

                // Clear finalized preview boxes immediately
                _overlayWindow.ClearFinalizedPreviews();
                _overlayWindow.SetClickThrough(true);
                return;
            }

            ApplyCutouts(_pendingCutouts.ToList());
            _pendingCutouts.Clear();

            _overlayWindow.ClearFinalizedPreviews();
            _overlayWindow.SetClickThrough(true);
            DebugLog.Write($"[App] Applied {_renderer.CutoutCount} total cutouts");
          }
          catch (Exception ex)
          {
            DebugLog.Write($"[App] ERROR in CtrlReleased: {ex}");
          }
        });
    }

    /// <summary>
    /// Adds the given cutout rects to the renderer, rebuilds the feathered mask,
    /// and animates the new cutouts fading in (unless it's the first batch).
    /// </summary>
    private void ApplyCutouts(List<System.Windows.Rect> cutouts)
    {
        if (_overlayWindow == null) return;

        foreach (var rect in cutouts)
            _renderer.AddCutout(rect);

        var overlaySize = new System.Windows.Size(_overlayWindow.ActualWidth, _overlayWindow.ActualHeight);
        var featheredMask = _renderer.BuildFeatheredMask(overlaySize);
        _overlayWindow.ApplyFeatheredMask(featheredMask);

        bool isFirstBatch = _overlayWindow.FadeInBackground();
        if (!isFirstBatch)
        {
            foreach (var rect in cutouts)
                _overlayWindow.AnimateCutoutFadeIn(rect);
        }

        _overlayWindow.ClearFinalizedPreviews();
        
        // Re-force toolbar above overlay after mask/fade changes
        ForceToolbarAboveOverlay();
        
        DebugLog.Write($"[App] Applied {_renderer.CutoutCount} total cutouts");
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
            _cachedScreenshot = null;
            _overlayWindow.BeginFadeOut(() =>
            {
                DebugLog.Write("[App] Fade-out complete, overlay hidden (cutouts and arrows preserved)");
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
            ForceToolbarAboveOverlay();
        });
    }
    /// <summary>
    /// Re-forces the flyout toolbar above the overlay in the z-order.
    /// Called after any operation that might push the overlay on top.
    /// </summary>
    private void ForceToolbarAboveOverlay()
    {
        DebugLog.Write($"[App] ForceToolbarAboveOverlay: toolbar={_flyoutToolbar != null}, visible={_flyoutToolbar?.IsVisible}, visibility={_flyoutToolbar?.Visibility}");
        if (_flyoutToolbar == null) return;
        var tbHwnd = new System.Windows.Interop.WindowInteropHelper(_flyoutToolbar).Handle;
        DebugLog.Write($"[App] ForceToolbarAboveOverlay: hwnd={tbHwnd}, Visibility={_flyoutToolbar.Visibility}");
        if (tbHwnd != IntPtr.Zero)
        {
            OverlayWindow.ForceTopmostHwnd(tbHwnd);
            DebugLog.Write($"[App] ForceToolbarAboveOverlay: DONE, forced hwnd={tbHwnd}");
        }
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

    // --- DismissStartMenu: synthetic mouse click to steal focus ---

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [System.Runtime.InteropServices.FieldOffset(0)]  public uint type;
        [System.Runtime.InteropServices.FieldOffset(8)]  public int dx;
        [System.Runtime.InteropServices.FieldOffset(12)] public int dy;
        [System.Runtime.InteropServices.FieldOffset(16)] public uint mouseData;
        [System.Runtime.InteropServices.FieldOffset(20)] public uint dwFlags;
        [System.Runtime.InteropServices.FieldOffset(24)] public uint time;
        [System.Runtime.InteropServices.FieldOffset(32)] public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int DISMISS_EXTRA_INFO = 0x534D4449;

    /// <summary>
    /// Dismisses the Start menu by injecting a synthetic left-click on our own
    /// overlay window (top-center pixel). The overlay absorbs the click so nothing
    /// underneath is affected. The Start menu loses focus and auto-closes.
    /// </summary>
    private static void DismissStartMenu(System.Windows.Rect monitorBounds)
    {
        try
        {
            var startMenu = FindWindow("Windows.UI.Core.CoreWindow", "Start");
            bool visible = startMenu != IntPtr.Zero && IsWindowVisible(startMenu);
            DebugLog.Write($"[App] DismissStartMenu: hwnd={startMenu}, visible={visible}");

            if (!visible) return;

            // Save cursor position
            GetCursorPos(out POINT origCursor);

            // Click at top-center of the monitor (where our overlay is).
            // The overlay is fullscreen on this monitor and topmost, so it absorbs the click.
            int clickX = (int)(monitorBounds.X + monitorBounds.Width / 2);
            int clickY = (int)monitorBounds.Y;

            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int screenH = GetSystemMetrics(SM_CYSCREEN);

            // Convert to absolute coordinates (0..65535 range)
            int absX = (int)Math.Round(clickX * 65535.0 / (screenW - 1));
            int absY = (int)Math.Round(clickY * 65535.0 / (screenH - 1));

            SetCursorPos(clickX, clickY);

            var inputs = new NativeInput[2];
            int size = System.Runtime.InteropServices.Marshal.SizeOf<NativeInput>();

            inputs[0].type = INPUT_MOUSE;
            inputs[0].dx = absX;
            inputs[0].dy = absY;
            inputs[0].dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;
            inputs[0].dwExtraInfo = (IntPtr)DISMISS_EXTRA_INFO;

            inputs[1].type = INPUT_MOUSE;
            inputs[1].dx = absX;
            inputs[1].dy = absY;
            inputs[1].dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTUP;
            inputs[1].dwExtraInfo = (IntPtr)DISMISS_EXTRA_INFO;

            uint sent = SendInput(2, inputs, size);

            // Restore cursor
            SetCursorPos(origCursor.X, origCursor.Y);

            DebugLog.Write($"[App] DismissStartMenu: click at {clickX},{clickY}, sent={sent}/2");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[App] DismissStartMenu failed (non-fatal): {ex.Message}");
        }
    }
}
