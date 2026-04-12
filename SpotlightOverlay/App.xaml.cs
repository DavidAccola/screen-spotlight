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
    private static SingleInstanceMessenger? _messenger;
    private SettingsService _settings = null!;
    private SpotlightRenderer _renderer = null!;
    private readonly ArrowRenderer _arrowRenderer = new();
    private readonly BoxRenderer _boxRenderer = new();
    private readonly HighlightRenderer _highlightRenderer = new();
    private readonly StepsRenderer _stepsRenderer = new();
    private GlobalInputHook _inputHook = null!;
    private TrayIconService _trayIcon = null!;
    private FlyoutToolbarWindow? _flyoutToolbar;
    private OverlayWindow? _overlayWindow;
    private readonly List<System.Windows.Rect> _pendingCutouts = new();
    private bool _isDismissed; // true when overlay is hidden but cutouts are preserved
    private System.Windows.Media.Imaging.BitmapSource? _cachedScreenshot; // pre-captured on Ctrl press
    // Undo stack for EscBehavior.UndoThenExit — tracks tool type of each finalized shape in order
    private readonly Stack<ToolType> _undoStack = new();
    private bool _undoConsumedLastEsc; // true after an undo Esc; next Esc exits

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance check — if already running, tell it to open settings then exit
        _singleInstanceMutex = new Mutex(true, "SpotlightOverlay_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Signal the running instance to open settings
            var messenger = new SingleInstanceMessenger();
            messenger.NotifyFirstInstance();
            Shutdown();
            return;
        }

        // Create the message window so subsequent launches can signal us
        _messenger = new SingleInstanceMessenger();
        _messenger.ActivateRequested += () => Dispatcher.BeginInvoke(() =>
            SettingsWindow.ShowSingleton(_settings, _inputHook));
        _messenger.CreateMessageWindow();
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

        // Message window is ready now that _settings and _inputHook are initialized
        _messenger!.CreateMessageWindow();

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
        _inputHook.ToggleToolModifier = _settings.ToggleToolModifier;
        _inputHook.ToggleToolKey = _settings.ToggleToolKey;
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
        _inputHook.ToggleToolRequested += OnToggleTool;
        _inputHook.ArrowDragUpdated += OnArrowDragUpdated;
        _inputHook.ArrowDragCompleted += OnArrowDragCompleted;
        _inputHook.BoxDragUpdated += OnBoxDragUpdated;
        _inputHook.BoxDragCompleted += OnBoxDragCompleted;
        _inputHook.HighlightDragUpdated += OnHighlightDragUpdated;
        _inputHook.HighlightDragCompleted += OnHighlightDragCompleted;
        _inputHook.StepsDragUpdated += OnStepsDragUpdated;
        _inputHook.StepsPlaced += OnStepsPlaced;

        // Keep hook in sync when settings change at runtime
        _inputHook.StepsShape = _settings.StepsShape;

        _settings.SettingsChanged += (_, _) =>
        {
            _inputHook.DragStyle = _settings.DragStyle;
            _inputHook.ActivationModifier = _settings.ActivationModifier;
            _inputHook.ActivationKey = _settings.ActivationKey;
            _inputHook.ToggleModifier = _settings.ToggleModifier;
            _inputHook.ToggleKey = _settings.ToggleKey;
            _inputHook.ToggleToolModifier = _settings.ToggleToolModifier;
            _inputHook.ToggleToolKey = _settings.ToggleToolKey;
            _inputHook.StepsShape = _settings.StepsShape;
            _trayIcon.SetToolbarVisible(_settings.FlyoutToolbarVisible);
        };

        // Create flyout toolbar (Req 7.2, 9.1, 11.1)
        try
        {
            _flyoutToolbar = new FlyoutToolbarWindow(_settings);
            _flyoutToolbar.ActiveToolChanged += OnActiveToolChanged;
            _flyoutToolbar.SetInputHook(_inputHook);
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
        _messenger?.Dispose();
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
            _overlayWindow?.HideBoxPreview();
            _overlayWindow?.HideHighlightPreview();
            _overlayWindow?.HideStepsPreview();
            _cachedScreenshot = null;

            // If there are no finalized shapes, close the overlay entirely
            // so the user doesn't need a second Esc to dismiss an empty overlay.
            bool hasFinalized = _renderer.CutoutCount > 0 || _arrowRenderer.ArrowCount > 0 || _boxRenderer.BoxCount > 0 || _highlightRenderer.HighlightCount > 0 || _stepsRenderer.StepCount > 0;
            if (!hasFinalized && _overlayWindow != null && !_isDismissed)
            {
                DebugLog.Write("[App] DragCancelled with empty overlay — auto-dismissing");
                _overlayWindow.Close();
                _overlayWindow = null;
                _pendingCutouts.Clear();
                _inputHook.CanRestore = false;
            }
        });
    }

    /// <summary>
    /// Clears all in-progress tool previews from the overlay without closing it.
    /// Called when switching tools mid-drag so the new tool starts fresh.
    /// </summary>
    private void HideAllPreviews()
    {
        if (_overlayWindow == null) return;
        _overlayWindow.HideDragPreview();
        _overlayWindow.HideArrowPreview();
        _overlayWindow.HideBoxPreview();
        _overlayWindow.HideHighlightPreview();
        _overlayWindow.HideStepsPreview();
    }

    /// <summary>
    /// Handles active tool changes from the flyout toolbar.
    /// </summary>
    private void OnActiveToolChanged(object? sender, ToolType tool)
    {
        _inputHook.ActiveTool = tool;
        DebugLog.Write($"[App] ActiveTool changed to {tool}");
        if (_inputHook.IsDragInProgress)
            Dispatcher.BeginInvoke(() => { HideAllPreviews(); _inputHook.RefreshDragPreview(); });
        if (_settings.ShowToolNameOnSwitch)
            Dispatcher.BeginInvoke(() => FlyoutNotification.ShowToolName(ToolDisplayName(tool)));
    }

    /// <summary>
    /// Cycles to the next tool in toolbar order (Spotlight → Arrow → ...).
    /// </summary>
    private void OnToggleTool(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_flyoutToolbar == null) return;
            var allTools = new[] { ToolType.Spotlight, ToolType.Arrow, ToolType.Box, ToolType.Highlight, ToolType.Steps };
            int current = Array.IndexOf(allTools, _flyoutToolbar.ActiveTool);
            var next = allTools[(current + 1) % allTools.Length];
            _flyoutToolbar.SetActiveToolExternal(next);
            _inputHook.ActiveTool = next;
            DebugLog.Write($"[App] ToggleTool: switched to {next}");
            if (_inputHook.IsDragInProgress)
            {
                HideAllPreviews();
                _inputHook.RefreshDragPreview();
            }
            if (_settings.ShowToolNameOnSwitch)
                FlyoutNotification.ShowToolName(ToolDisplayName(next));
        });
    }

    private static string ToolDisplayName(ToolType tool) => tool switch
    {
        ToolType.Spotlight => "Spotlight",
        ToolType.Arrow     => "Arrow",
        ToolType.Box       => "Box",
        ToolType.Highlight => "Highlight",
        ToolType.Steps     => "Steps",
        _                  => tool.ToString()
    };

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
            if (_inputHook.ActiveTool == ToolType.Spotlight && _settings.FadeMode == FadeMode.Immediately)
                win.FadeInBackground(300);
            win.ForceTopmost();
            DismissStartMenu(win);
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
        _overlayWindow.ClearBoxes();
        _overlayWindow.ClearSteps();
        _overlayWindow.Close();
        _overlayWindow = null;
        _renderer.ClearCutouts();
        _arrowRenderer.ClearArrows();
        _boxRenderer.ClearBoxes();
        _highlightRenderer.ClearHighlights();
        _stepsRenderer.ClearSteps();
        _pendingCutouts.Clear();
        _undoStack.Clear();
        _undoConsumedLastEsc = false;
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

                var previewPath = _arrowRenderer.BuildArrowPath(dipStart, dipEnd, color, leftEnd, rightEnd, lineStyle,
                    _settings.ArrowLeftEndSize, _settings.ArrowLineThickness, _settings.ArrowRightEndSize);
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
                var shadowPath = _arrowRenderer.BuildShadowPath(dipStart, dipEnd, leftEnd, rightEnd, lineStyle,
                    _settings.ArrowLeftEndSize, _settings.ArrowLineThickness, _settings.ArrowRightEndSize);

                // Add main arrow path
                var mainPath = _arrowRenderer.BuildArrowPath(dipStart, dipEnd, color, leftEnd, rightEnd, lineStyle,
                    _settings.ArrowLeftEndSize, _settings.ArrowLineThickness, _settings.ArrowRightEndSize);
                if (mainPath != null)
                {
                    _overlayWindow.BeginArrowGroup();
                    if (shadowPath != null) _overlayWindow.AddArrowVisualGrouped(shadowPath);
                    _overlayWindow.AddArrowVisualGrouped(mainPath);
                    _arrowRenderer.AddArrow(dipStart, dipEnd);
                    _undoStack.Push(ToolType.Arrow);
                    _undoConsumedLastEsc = false;
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
    /// Handles live box drag updates: shows a preview rectangle on the overlay.
    /// </summary>
    private void OnBoxDragUpdated(object? sender, DragRectEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ClearDismissedState();

                if (!EnsureOverlayWindow(e.DragStartPoint)) return;

                var dipRect = ScreenRectToWindowDip(e.ScreenRect);
                var color = ParseBoxColor();

                var shadowPath = _boxRenderer.BuildShadowPath(dipRect, _settings.BoxLineThickness);
                if (shadowPath != null)
                    _overlayWindow!.ShowBoxPreview(shadowPath);

                var mainPath = _boxRenderer.BuildBoxPath(dipRect, color, _settings.BoxLineThickness);
                if (mainPath != null)
                    _overlayWindow!.ShowBoxPreview(mainPath);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in BoxDragUpdated: {ex}");
            }
        });
    }

    /// <summary>
    /// Handles a completed box drag: adds the finalized box (shadow + main) to the overlay.
    /// </summary>
    private void OnBoxDragCompleted(object? sender, DragRectEventArgs e)
    {
        DebugLog.Write($"[App] OnBoxDragCompleted: ScreenRect={e.ScreenRect}");
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_overlayWindow == null) return;

                _overlayWindow.HideBoxPreview();

                var dipRect = ScreenRectToWindowDip(e.ScreenRect);
                var color = ParseBoxColor();

                // Add shadow first (renders behind the main box)
                var shadowPath = _boxRenderer.BuildShadowPath(dipRect, _settings.BoxLineThickness);
                if (shadowPath != null)
                    _overlayWindow.AddBoxVisual(shadowPath);

                // Add main box path
                var mainPath = _boxRenderer.BuildBoxPath(dipRect, color, _settings.BoxLineThickness);
                if (mainPath != null)
                {
                    _overlayWindow.AddBoxVisual(mainPath);
                    _boxRenderer.AddBox(dipRect);
                    _undoStack.Push(ToolType.Box);
                    _undoConsumedLastEsc = false;
                }

                // Note: do NOT call FadeInBackground() — boxes render on transparent overlay like arrows.
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in BoxDragCompleted: {ex}");
            }
        });
    }

    /// <summary>
    /// Parses the BoxColor hex string from settings into a WPF Color.
    /// Falls back to a cyan-blue if parsing fails.
    /// </summary>
    private System.Windows.Media.Color ParseBoxColor()
    {
        try
        {
            var hex = _settings.BoxColor;
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return System.Windows.Media.Color.FromRgb(0x00, 0xB4, 0xFF);
    }
    private System.Windows.Media.Color ParseHighlightColor()
    {
        try
        {
            var hex = _settings.HighlightColor;
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return System.Windows.Media.Color.FromRgb(0xFF, 0xC9, 0x0E);
    }

    private System.Windows.Media.Color ParseStepsFillColor()
    {
        try
        {
            var hex = _settings.StepsFillColor;
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return System.Windows.Media.Color.FromRgb(0xE8, 0x40, 0x40);
    }

    private System.Windows.Media.Color ParseStepsOutlineColor()
    {
        try
        {
            var hex = _settings.StepsOutlineColor;
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

    private System.Windows.Media.Color ParseStepsFontColor()
    {
        try
        {
            var hex = _settings.StepsFontColor;
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

    /// <summary>
    /// For teardrop mode: the user clicks the tip and drags to set direction.
    /// anchorDip (first click) = tip position.
    /// We compute the circle center by moving from tip in the OPPOSITE direction of the drag.
    /// tailAngleRad = angle from circle center toward tip = angle of anchor→release drag.
    /// For circle mode: anchorDip = circle center (anchor == release, single click).
    /// When modifierHeld is false (mouse-only), snaps tail angle to nearest 90°.
    /// </summary>
    private (System.Windows.Point circleCenterDip, double tailAngleRad) ComputeStepsGeometry(
        System.Windows.Point anchorDip, System.Windows.Point releaseDip, bool modifierHeld)
    {
        // SnapToCardinal setting always snaps regardless of modifier state
        bool snap = modifierHeld || _settings.StepsTailDirection == StepsTailDirection.SnapToCardinal;
        if (_settings.StepsShape == Models.StepsShape.Circle || anchorDip == releaseDip)
            return (anchorDip, 0);

        double rawAngle = Math.Atan2(releaseDip.Y - anchorDip.Y, releaseDip.X - anchorDip.X);

        double tailAngleRad = snap
            ? Math.Round(rawAngle / (Math.PI / 2)) * (Math.PI / 2) // snap to 0, ±π/2, π
            : rawAngle;

        // Tip is at anchorDip. Circle center is behind the tip by (radius + tailLength).
        double radius = _settings.StepsSize / 2.0;
        double tailLength = _settings.StepsSize * StepsRenderer.TailLengthFactor;
        double dist = radius + tailLength;

        var circleCenterDip = new System.Windows.Point(
            anchorDip.X - dist * Math.Cos(tailAngleRad),
            anchorDip.Y - dist * Math.Sin(tailAngleRad));

        return (circleCenterDip, tailAngleRad);
    }

    private void OnHighlightDragUpdated(object? sender, DragRectEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ClearDismissedState();
                if (!EnsureOverlayWindow(e.DragStartPoint)) return;
                _overlayWindow!.SetHighlightOpacity(_settings.HighlightOpacity);
                var dipRect = ScreenRectToWindowDip(e.ScreenRect);
                var color = ParseHighlightColor();
                var previewPath = _highlightRenderer.BuildHighlightPath(dipRect, color);
                if (previewPath != null)
                    _overlayWindow.ShowHighlightPreview(previewPath);
            }
            catch (Exception ex) { DebugLog.Write($"[App] ERROR in HighlightDragUpdated: {ex}"); }
        });
    }

    private void OnHighlightDragCompleted(object? sender, DragRectEventArgs e)
    {
        DebugLog.Write($"[App] OnHighlightDragCompleted: ScreenRect={e.ScreenRect}");
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_overlayWindow == null) return;
                _overlayWindow.HideHighlightPreview();
                _overlayWindow.SetHighlightOpacity(_settings.HighlightOpacity);
                var dipRect = ScreenRectToWindowDip(e.ScreenRect);
                var color = ParseHighlightColor();
                var mainPath = _highlightRenderer.BuildHighlightPath(dipRect, color);
                if (mainPath != null)
                {
                    _overlayWindow.AddHighlightVisual(mainPath);
                    _highlightRenderer.AddHighlight(dipRect);
                    _undoStack.Push(ToolType.Highlight);
                    _undoConsumedLastEsc = false;
                }
            }
            catch (Exception ex) { DebugLog.Write($"[App] ERROR in HighlightDragCompleted: {ex}"); }
        });
    }

    private void OnStepsDragUpdated(object? sender, StepsPlacedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ClearDismissedState();
                if (!EnsureOverlayWindow(e.AnchorPoint)) return;

                var anchorDip = _overlayWindow!.PointFromScreen(e.AnchorPoint);
                var releaseDip = _overlayWindow.PointFromScreen(e.ReleasePoint);

                var (circleCenterDip, tailAngleRad) = ComputeStepsGeometry(anchorDip, releaseDip, e.ModifierHeld);

                var fillColor = ParseStepsFillColor();
                var outlineColor = ParseStepsOutlineColor();
                var fontColor = ParseStepsFontColor();
                var options = new StepsRenderOptions(
                    _settings.StepsShape,
                    _settings.StepsOutlineEnabled,
                    _settings.StepsSize,
                    fillColor,
                    outlineColor,
                    _settings.StepsFontFamily,
                    _settings.StepsFontSize,
                    _settings.StepsFontBold,
                    fontColor);

                var visual = _stepsRenderer.BuildStepVisual(circleCenterDip, tailAngleRad, _stepsRenderer.NextStepNumber, options);
                _overlayWindow.ShowStepsPreview(visual);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in StepsDragUpdated: {ex}");
            }
        });
    }

    private void OnStepsPlaced(object? sender, StepsPlacedEventArgs e)
    {
        DebugLog.Write($"[App] OnStepsPlaced: Anchor={e.AnchorPoint}, Release={e.ReleasePoint}");
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_overlayWindow == null) return;

                _overlayWindow.HideStepsPreview();

                var anchorDip = _overlayWindow.PointFromScreen(e.AnchorPoint);
                var releaseDip = _overlayWindow.PointFromScreen(e.ReleasePoint);

                var (circleCenterDip, tailAngleRad) = ComputeStepsGeometry(anchorDip, releaseDip, e.ModifierHeld);

                var fillColor = ParseStepsFillColor();
                var outlineColor = ParseStepsOutlineColor();
                var fontColor = ParseStepsFontColor();
                var options = new StepsRenderOptions(
                    _settings.StepsShape,
                    _settings.StepsOutlineEnabled,
                    _settings.StepsSize,
                    fillColor,
                    outlineColor,
                    _settings.StepsFontFamily,
                    _settings.StepsFontSize,
                    _settings.StepsFontBold,
                    fontColor);

                int stepNumber = _stepsRenderer.NextStepNumber;
                var visual = _stepsRenderer.BuildStepVisual(circleCenterDip, tailAngleRad, stepNumber, options);
                _overlayWindow.AddStepVisual(visual);
                _stepsRenderer.AddStep(circleCenterDip, tailAngleRad, stepNumber, options);
                _undoStack.Push(ToolType.Steps);
                _undoConsumedLastEsc = false;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[App] ERROR in StepsPlaced: {ex}");
            }
        });
    }

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

        // Capture existing cutouts BEFORE adding new ones — used to clip the fade animation
        var existingCutouts = _renderer.Cutouts.ToList();

        foreach (var rect in cutouts)
        {
            _renderer.AddCutout(rect);
            _undoStack.Push(ToolType.Spotlight);
            _undoConsumedLastEsc = false;
        }

        var overlaySize = new System.Windows.Size(_overlayWindow.ActualWidth, _overlayWindow.ActualHeight);
        var featheredMask = _renderer.BuildFeatheredMask(overlaySize);
        _overlayWindow.ApplyFeatheredMask(featheredMask);

        bool isFirstBatch = _overlayWindow.FadeInBackground();
        if (!isFirstBatch)
        {
            foreach (var rect in cutouts)
                _overlayWindow.AnimateCutoutFadeIn(rect, existingCutouts);
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

            // UndoThenExit: first Esc undos last shape; second Esc (with nothing new created) exits
            if (_settings.EscBehavior == EscBehavior.UndoThenExit && _undoStack.Count > 1 && !_undoConsumedLastEsc)
            {
                DebugLog.Write($"[App] Esc undo: removing last shape ({_undoStack.Peek()})");
                UndoLastShape();
                _undoConsumedLastEsc = true;
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

    /// <summary>
    /// Removes the most recently created shape from the overlay and its renderer.
    /// </summary>
    private void UndoLastShape()
    {
        if (_overlayWindow == null || _undoStack.Count == 0) return;
        var tool = _undoStack.Pop();
        switch (tool)
        {
            case ToolType.Spotlight:
                if (_renderer.CutoutCount > 0)
                {
                    var lastCutout = _renderer.Cutouts[_renderer.CutoutCount - 1];
                    _renderer.RemoveLastCutout();
                    var overlaySize = new System.Windows.Size(_overlayWindow.ActualWidth, _overlayWindow.ActualHeight);
                    var remaining = _renderer.Cutouts.ToList();
                    _overlayWindow.AnimateCutoutsFadeOut(
                        new[] { lastCutout },
                        () => _overlayWindow?.ApplyFeatheredMask(_renderer.BuildFeatheredMask(overlaySize)),
                        durationMs: 300,
                        remainingCutouts: remaining);
                }
                break;
            case ToolType.Arrow:
                _overlayWindow.AnimateRemoveLastArrow();
                if (_arrowRenderer.ArrowCount > 0)
                    _arrowRenderer.RemoveLastArrow();
                break;
            case ToolType.Box:
                _overlayWindow.AnimateRemoveLastBox();
                if (_boxRenderer.BoxCount > 0)
                    _boxRenderer.RemoveLastBox();
                break;
            case ToolType.Highlight:
                _overlayWindow.AnimateRemoveLastHighlight();
                if (_highlightRenderer.HighlightCount > 0)
                    _highlightRenderer.RemoveLastHighlight();
                break;
            case ToolType.Steps:
                _overlayWindow.AnimateRemoveLastStep();
                if (_stepsRenderer.StepCount > 0)
                    _stepsRenderer.RemoveLastStep();
                break;
        }
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

    // --- DismissStartMenu: focus-steal → click fallback ---

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint GA_ROOT = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BlockInput(bool fBlockIt);

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

    private const uint ASFW_ANY = 0xFFFFFFFF;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int DISMISS_EXTRA_INFO = 0x534D4449;

    /// <summary>
    /// Dismisses the Windows 11 Start menu.
    /// Tries focus-steal first (no flicker). Falls back to synthetic click (BlockInput suppresses flicker).
    /// </summary>
    private static void DismissStartMenu(OverlayWindow win)
    {
        try
        {
            if (!IsStartMenuOpen())
            {
                DebugLog.Write("[App] DismissStartMenu: Start menu not open, skipping");
                return;
            }

            DebugLog.Write("[App] DismissStartMenu: Start menu is open");

            // Try focus-steal first — no cursor movement, no flicker
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (hwnd != IntPtr.Zero)
            {
                AllowSetForegroundWindow(ASFW_ANY);
                if (SetForegroundWindow(hwnd))
                {
                    DebugLog.Write("[App] DismissStartMenu: dismissed via focus-steal");
                    return;
                }
            }

            // Fallback: synthetic click at top-center of screen (overlay absorbs it)
            DebugLog.Write("[App] DismissStartMenu: focus-steal failed, using click fallback");
            GetCursorPos(out POINT origCursor);
            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int screenH = GetSystemMetrics(SM_CYSCREEN);
            int clickX = screenW / 2;
            int clickY = 0;
            int absX = (int)Math.Round(clickX * 65535.0 / (screenW - 1));
            int absY = 0;

            var inputs = new NativeInput[2];
            int size = System.Runtime.InteropServices.Marshal.SizeOf<NativeInput>();
            inputs[0].type = INPUT_MOUSE; inputs[0].dx = absX; inputs[0].dy = absY;
            inputs[0].dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;
            inputs[0].dwExtraInfo = (IntPtr)DISMISS_EXTRA_INFO;
            inputs[1].type = INPUT_MOUSE; inputs[1].dx = absX; inputs[1].dy = absY;
            inputs[1].dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTUP;
            inputs[1].dwExtraInfo = (IntPtr)DISMISS_EXTRA_INFO;

            BlockInput(true);
            try
            {
                SetCursorPos(clickX, clickY);
                SendInput(2, inputs, size);
                SetCursorPos(origCursor.X, origCursor.Y);
            }
            finally { BlockInput(false); }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[App] DismissStartMenu failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Detects whether the Windows 11 Start menu is currently open and visible
    /// by hit-testing a point in the center/upper area of the primary monitor.
    /// If the window under that point belongs to StartMenuExperienceHost, Start is open.
    /// This is more reliable than UIA or IsWindowVisible — it reflects what's actually rendered.
    /// </summary>
    private static bool IsStartMenuOpen()
    {
        try
        {
            // Hit-test at center-top of primary work area — Start menu always covers this region
            var workArea = SystemParameters.WorkArea;
            var pt = new POINT
            {
                X = (int)(workArea.Left + workArea.Width / 2),
                Y = (int)(workArea.Top + workArea.Height / 3)
            };

            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;

            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;

            GetWindowThreadProcessId(root, out uint pid);
            if (pid == 0) return false;

            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            bool isStart = proc.ProcessName.Equals("StartMenuExperienceHost",
                StringComparison.OrdinalIgnoreCase);
            DebugLog.Write($"[App] IsStartMenuOpen: process={proc.ProcessName} → {isStart}");
            return isStart;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[App] IsStartMenuOpen failed: {ex.Message}");
            return false;
        }
    }
}
