using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpotlightOverlay.Windows;

/// <summary>
/// Flyout toolbar window — docks to a screen edge with a small nub handle.
/// The nub is always visible; hovering expands the full toolbar.
/// The nub is draggable to reposition along the anchored edge.
/// </summary>
public partial class FlyoutToolbarWindow : Window
{
    // Nub dimensions
    private const double NubWidth = 14.0;   // narrow dimension (perpendicular to edge)
    private const double NubLength = 52.0;  // along-edge dimension (about 1 button height)

    private const int ExpandDurationMs = 200;
    private const int CollapseDurationMs = 200;
    private const int CollapseDelayMs = 300;

    // Accent color for the active tool button highlight
    private static readonly SolidColorBrush ActiveToolBrush = new(Color.FromRgb(0x2E, 0x6E, 0xB5));

    private readonly SettingsService _settings;
    private AnchorEdge _anchorEdge;
    private bool _isExpanded;
    private DispatcherTimer? _collapseTimer;
    private bool _lastVisibility;

    /// <summary>
    /// The currently selected annotation tool. Defaults to Spotlight.
    /// </summary>
    public ToolType ActiveTool { get; private set; } = ToolType.Spotlight;

    /// <summary>
    /// Raised when the user selects a different tool in the toolbar.
    /// </summary>
    public event EventHandler<ToolType>? ActiveToolChanged;

    /// <summary>
    /// Raised when the user clicks the Dismiss toolbar button.
    /// </summary>
    public event EventHandler? DismissToolbarRequested;

    // Nub offset along the toolbar face (set by NubOffsetCalculator during drag).
    // This is the distance from the toolbar's start edge to the nub, used when expanded.
    private double _nubOffset;

    // The nub's screen position along the edge axis (Y for Left/Right, X for Top).
    // This is the source of truth — the nub never moves, the toolbar positions around it.
    private double _nubScreenPos;

    // Drag-to-reposition state
    private bool _isDragging;
    private Point _dragStartScreen;

    // Cached toolbar panel size (measured after layout)
    private double _toolbarWidth;
    private double _toolbarHeight;

    // Fullscreen detection
    private System.Windows.Threading.DispatcherTimer? _fullscreenCheckTimer;
    private bool _hiddenDueToFullscreen = false;
    private bool _expandedOverFullscreen = false; // toolbar is temporarily shown over a fullscreen app

    // WinEvent hooks for instant fullscreen detection
    private IntPtr _hookForeground = IntPtr.Zero;
    private IntPtr _hookLocationChange = IntPtr.Zero;
    private WinEventDelegate? _winEventDelegate; // must be kept alive to prevent GC
    private System.Windows.Threading.DispatcherTimer? _debounceTimer;

    // Reference to the input hook for checking tool-in-progress state
    private Input.GlobalInputHook? _inputHook;

    public FlyoutToolbarWindow(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

        _anchorEdge = _settings.ToolbarAnchorEdge;
        _lastVisibility = _settings.FlyoutToolbarVisible;

        // Wire events
        NubHandle.MouseEnter += NubHandle_MouseEnter;
        NubHandle.MouseLeftButtonDown += NubHandle_MouseLeftButtonDown;
        NubHandle.MouseLeftButtonUp += NubHandle_MouseLeftButtonUp;
        NubHandle.MouseMove += NubHandle_MouseMove;
        DragZoneStart.MouseLeftButtonDown += NubHandle_MouseLeftButtonDown;
        DragZoneStart.MouseLeftButtonUp += NubHandle_MouseLeftButtonUp;
        DragZoneStart.MouseMove += NubHandle_MouseMove;
        DragZoneEnd.MouseLeftButtonDown += NubHandle_MouseLeftButtonDown;
        DragZoneEnd.MouseLeftButtonUp += NubHandle_MouseLeftButtonUp;
        DragZoneEnd.MouseMove += NubHandle_MouseMove;
        ToolbarDragBorder.MouseLeftButtonDown += NubHandle_MouseLeftButtonDown;
        ToolbarDragBorder.MouseLeftButtonUp += NubHandle_MouseLeftButtonUp;
        ToolbarDragBorder.MouseMove += NubHandle_MouseMove;
        MouseEnter += Window_MouseEnter;
        MouseLeave += Window_MouseLeave;

        SpotlightButton.Click += SpotlightButton_Click;
        ArrowButton.Click += ArrowButton_Click;
        StepsButton.Click += StepsButton_Click;
        HighlightButton.Click += HighlightButton_Click;
        BoxButton.Click += BoxButton_Click;
        SettingsButton.Click += SettingsButton_Click;
        DismissToolbarButton.Click += DismissToolbarButton_Click;
        DismissToolbarButton.MouseEnter += (_, _) =>
        {
            if (DismissToolbarButton.Content is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        };
        DismissToolbarButton.MouseLeave += (_, _) =>
        {
            if (DismissToolbarButton.Content is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        };
        UpdateDismissTooltip();

        _settings.SettingsChanged += OnSettingsChanged;

        Loaded += (_, _) =>
        {
            ConfigureLayout(_anchorEdge);
            // Measure toolbar panel to get its natural size
            ToolbarPanel.Visibility = Visibility.Visible;
            ToolbarPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _toolbarWidth = ToolbarPanel.DesiredSize.Width;
            _toolbarHeight = ToolbarPanel.DesiredSize.Height;
            ToolbarPanel.Visibility = Visibility.Collapsed;

            // Restore saved nub position
            var saved = new SavedNubState(_settings.NubFraction, _settings.NubAnchorEdge, _settings.NubMonitorFingerprint);
            var monitors = MonitorHelper.GetAllMonitors();
            var resolved = NubPositionValidator.Resolve(saved, monitors, NubLength);

            if (resolved.AnchorEdge != _anchorEdge)
            {
                _anchorEdge = resolved.AnchorEdge;
                _settings.ToolbarAnchorEdge = resolved.AnchorEdge;
                ConfigureLayout(resolved.AnchorEdge);
                ToolbarPanel.Visibility = Visibility.Visible;
                ToolbarPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _toolbarWidth = ToolbarPanel.DesiredSize.Width;
                _toolbarHeight = ToolbarPanel.DesiredSize.Height;
                ToolbarPanel.Visibility = Visibility.Collapsed;
            }

            _nubScreenPos = resolved.NubScreenPos;
            PositionCollapsed(resolved.WorkArea);
            DebugLog.Write($"[Toolbar] Startup restore: edge={resolved.AnchorEdge} pos={resolved.NubScreenPos:F1} workArea={resolved.WorkArea} savedFraction={_settings.NubFraction} savedFingerprint={_settings.NubMonitorFingerprint}");

            // Highlight the default active tool (Spotlight)
            HighlightActiveToolButton();

            // Apply saved tool order
            ApplyToolOrder();

            // Generate the spotlight icon using the actual renderer pipeline
            SpotlightIcon.Source = BuildSpotlightIconBitmap(16, 13, featherRadius: 3);

            // Start fullscreen detection timer
            StartFullscreenDetection();
        };
    }

    /// <summary>
    /// Configures the Grid layout (column/row placement of nub and toolbar)
    /// based on the anchor edge.
    /// </summary>
    private void ConfigureLayout(AnchorEdge edge)
    {
        _anchorEdge = edge;

        // Reset grid definitions
        Col0.Width = new GridLength(0, GridUnitType.Auto);
        Col1.Width = new GridLength(0, GridUnitType.Auto);
        Row0.Height = new GridLength(0, GridUnitType.Auto);
        Row1.Height = new GridLength(0, GridUnitType.Auto);

        switch (edge)
        {
            case AnchorEdge.Right:
                // [NubHandle | ToolbarPanel] — nub faces inward (left), toolbar against right edge
                NubHandle.Width = NubWidth;
                NubHandle.Height = NubLength;
                NubHandle.CornerRadius = new CornerRadius(4, 0, 0, 4);
                NubHandle.VerticalAlignment = VerticalAlignment.Top;
                ToolbarPanel.CornerRadius = new CornerRadius(0, 6, 6, 6); // flat top-left where nub connects
                GripDots.Text = "⋮";
                ButtonPanel.Orientation = Orientation.Vertical;
                Separator.Height = 1;
                Separator.Width = double.NaN;

                Grid.SetColumn(NubHandle, 0);
                Grid.SetRow(NubHandle, 0);
                Grid.SetColumn(ToolbarPanel, 1);
                Grid.SetRow(ToolbarPanel, 0);
                Grid.SetRowSpan(ToolbarPanel, 1);
                Grid.SetColumnSpan(ToolbarPanel, 1);
                Grid.SetRowSpan(NubHandle, 1);
                Grid.SetColumnSpan(NubHandle, 1);
                break;

            case AnchorEdge.Left:
                // [ToolbarPanel | NubHandle] — nub faces inward (right), toolbar against left edge
                NubHandle.Width = NubWidth;
                NubHandle.Height = NubLength;
                NubHandle.CornerRadius = new CornerRadius(0, 4, 4, 0);
                NubHandle.VerticalAlignment = VerticalAlignment.Top;
                ToolbarPanel.CornerRadius = new CornerRadius(6, 0, 6, 6); // flat top-right where nub connects
                GripDots.Text = "⋮";
                ButtonPanel.Orientation = Orientation.Vertical;
                Separator.Height = 1;
                Separator.Width = double.NaN;

                Grid.SetColumn(ToolbarPanel, 0);
                Grid.SetRow(ToolbarPanel, 0);
                Grid.SetColumn(NubHandle, 1);
                Grid.SetRow(NubHandle, 0);
                Grid.SetRowSpan(ToolbarPanel, 1);
                Grid.SetColumnSpan(ToolbarPanel, 1);
                Grid.SetRowSpan(NubHandle, 1);
                Grid.SetColumnSpan(NubHandle, 1);
                break;

            case AnchorEdge.Top:
                // [ToolbarPanel]  — toolbar against top edge
                // [NubHandle]     — nub faces inward (bottom)
                NubHandle.Width = NubLength;
                NubHandle.Height = NubWidth;
                NubHandle.CornerRadius = new CornerRadius(0, 0, 4, 4);
                NubHandle.VerticalAlignment = VerticalAlignment.Top;
                ToolbarPanel.CornerRadius = new CornerRadius(6, 6, 6, 0); // flat bottom-left where nub connects
                GripDots.Text = "⋯";
                ButtonPanel.Orientation = Orientation.Horizontal;
                Separator.Height = double.NaN;
                Separator.Width = 1;

                Grid.SetColumn(ToolbarPanel, 0);
                Grid.SetRow(ToolbarPanel, 0);
                Grid.SetColumn(NubHandle, 0);
                Grid.SetRow(NubHandle, 1);
                Grid.SetRowSpan(ToolbarPanel, 1);
                Grid.SetColumnSpan(ToolbarPanel, 1);
                Grid.SetRowSpan(NubHandle, 1);
                Grid.SetColumnSpan(NubHandle, 1);
                break;
        }

        ApplyNubOffset();

        // Configure drag zone dimensions — half the nub width (7px)
        double dragZoneSize = NubWidth / 2;
        if (edge == AnchorEdge.Top)
        {
            DragZoneStart.Width = dragZoneSize;
            DragZoneStart.Height = double.NaN;
            DragZoneEnd.Width = dragZoneSize;
            DragZoneEnd.Height = double.NaN;
            ToolbarDragBorder.Padding = new Thickness(0, dragZoneSize, 0, dragZoneSize);
        }
        else
        {
            DragZoneStart.Width = double.NaN;
            DragZoneStart.Height = dragZoneSize;
            DragZoneEnd.Width = double.NaN;
            DragZoneEnd.Height = dragZoneSize;
            ToolbarDragBorder.Padding = new Thickness(dragZoneSize, 0, dragZoneSize, 0);
        }
    }

    private void ApplyNubOffset()
    {
        // Only apply nub margin when the toolbar panel is visible (expanded).
        // When collapsed, the window IS the nub — margin would push it off-position.
        if (ToolbarPanel.Visibility != Visibility.Visible)
        {
            NubHandle.Margin = new Thickness(0);
            return;
        }

        switch (_anchorEdge)
        {
            case AnchorEdge.Left:
            case AnchorEdge.Right:
                NubHandle.VerticalAlignment = VerticalAlignment.Top;
                NubHandle.Margin = new Thickness(0, _nubOffset, 0, 0);
                break;
            case AnchorEdge.Top:
                NubHandle.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                NubHandle.Margin = new Thickness(_nubOffset, 0, 0, 0);
                break;
        }
    }

    /// <summary>
    /// Positions the window so only the nub is visible at the screen edge.
    /// The toolbar panel is collapsed (hidden).
    /// When <paramref name="workAreaOverride"/> is null, centers the nub on the primary work area edge.
    /// When provided (startup restore), uses the existing <c>_nubScreenPos</c> (already set to the resolved value).
    /// </summary>
    private void PositionCollapsed(Rect? workAreaOverride = null)
    {
        _nubOffset = 0;
        ToolbarPanel.Visibility = Visibility.Collapsed;
        _isExpanded = false;

        var workArea = workAreaOverride ?? SystemParameters.WorkArea;

        // Size the window to just the nub
        SizeToContent = SizeToContent.WidthAndHeight;
        UpdateLayout();

        if (workAreaOverride is null)
        {
            // Default: center the nub on the work area edge
            switch (_anchorEdge)
            {
                case AnchorEdge.Right:
                    Left = workArea.Right - NubWidth;
                    Top = workArea.Top + (workArea.Height - NubLength) / 2;
                    _nubScreenPos = Top;
                    break;
                case AnchorEdge.Left:
                    Left = workArea.Left;
                    Top = workArea.Top + (workArea.Height - NubLength) / 2;
                    _nubScreenPos = Top;
                    break;
                case AnchorEdge.Top:
                    Left = workArea.Left + (workArea.Width - NubLength) / 2;
                    Top = workArea.Top;
                    _nubScreenPos = Left;
                    break;
            }
        }
        else
        {
            // Restore: _nubScreenPos is already set to the resolved value; just position the window
            switch (_anchorEdge)
            {
                case AnchorEdge.Right:
                    Left = workArea.Right - NubWidth;
                    Top = _nubScreenPos;
                    break;
                case AnchorEdge.Left:
                    Left = workArea.Left;
                    Top = _nubScreenPos;
                    break;
                case AnchorEdge.Top:
                    Left = _nubScreenPos;
                    Top = workArea.Top;
                    break;
            }
        }

        ComputeNubOffsetForExpand();
    }

    public void UpdateAnchorEdge(AnchorEdge edge)
    {
        ConfigureLayout(edge);

        // Re-measure toolbar
        ToolbarPanel.Visibility = Visibility.Visible;
        ToolbarPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _toolbarWidth = ToolbarPanel.DesiredSize.Width;
        _toolbarHeight = ToolbarPanel.DesiredSize.Height;
        ToolbarPanel.Visibility = Visibility.Collapsed;

        PositionCollapsed();
    }

    public void ShowToolbar() => Visibility = Visibility.Visible;
    public void HideToolbar() => Visibility = Visibility.Collapsed;

    /// <summary>
    /// Provides the input hook so the toolbar can check IsDragInProgress before expanding,
    /// and receive global mouse move events for nub hover detection while fullscreen-hidden.
    /// </summary>
    public void SetInputHook(Input.GlobalInputHook hook)
    {
        _inputHook = hook;
        _inputHook.MouseMoved += OnGlobalMouseMoved;
    }

    /// <summary>
    /// Called on every global mouse move. When the toolbar is fullscreen-hidden (opacity=0),
    /// checks if the cursor is over the nub's screen rect and expands if so.
    /// </summary>
    private void OnGlobalMouseMoved(object? sender, System.Windows.Point physicalPt)
    {
        if (!_hiddenDueToFullscreen) return;
        if (_inputHook?.IsDragInProgress == true) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_hiddenDueToFullscreen) return;
            if (_inputHook?.IsDragInProgress == true) return;

            bool overNub = GetNubPhysicalRect().Contains(physicalPt);

            if (overNub && !_expandedOverFullscreen)
            {
                // Cursor entered nub — fade in and expand
                _expandedOverFullscreen = true;
                FadeOpacity(0.0, 1.0);
                if (!_isExpanded && !_isDragging)
                    ExpandToolbar();
            }
            else if (!overNub && _expandedOverFullscreen && !_isExpanded)
            {
                // Cursor left nub area and toolbar has since collapsed — fade back out
                _expandedOverFullscreen = false;
                FadeOpacity(1.0, 0.0);
            }
        });
    }

    /// <summary>
    /// Returns the nub's bounding rect in physical screen pixels.
    /// </summary>
    private System.Windows.Rect GetNubPhysicalRect()
    {
        var monitorPoint = new System.Windows.Point(Left, Top);
        double scale = MonitorHelper.GetDpiScale(monitorPoint);
        return new System.Windows.Rect(Left * scale, Top * scale, NubWidth * scale, NubLength * scale);
    }

    private void FadeOpacity(double from, double to)
    {
        BeginAnimation(OpacityProperty, null);
        var fade = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = to > from ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // ── Expand / Collapse ────────────────────────────────────────────

    private void ExpandToolbar()
    {
        if (_isExpanded) return;

        try
        {
            _collapseTimer?.Stop();

            // Show the toolbar panel
            ToolbarPanel.Visibility = Visibility.Visible;
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();

            var workArea = GetWorkAreaForNub();

            // Compute expanded window position so nub stays at _nubScreenPos
            switch (_anchorEdge)
            {
                case AnchorEdge.Right:
                    Left = workArea.Right - ActualWidth;
                    Top = _nubScreenPos - _nubOffset;
                    Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - ActualHeight);
                    _nubOffset = _nubScreenPos - Top;
                    break;
                case AnchorEdge.Left:
                    Left = workArea.Left;
                    Top = _nubScreenPos - _nubOffset;
                    Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - ActualHeight);
                    _nubOffset = _nubScreenPos - Top;
                    break;
                case AnchorEdge.Top:
                    Top = workArea.Top;
                    Left = _nubScreenPos - _nubOffset;
                    Left = Math.Clamp(Left, workArea.Left, workArea.Right - ActualWidth);
                    _nubOffset = _nubScreenPos - Left;
                    break;
            }

            ApplyNubOffset();
            _isExpanded = true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Toolbar] Expand error: {ex.Message}");
            ToolbarPanel.Visibility = Visibility.Visible;
            _isExpanded = true;
        }
    }

    private void CollapseToolbar()
    {
        if (!_isExpanded) return;

        try
        {
            _isExpanded = false;
            BeginAnimation(Window.LeftProperty, null);
            BeginAnimation(Window.TopProperty, null);
            NubHandle.Margin = new Thickness(0);
            ToolbarPanel.Visibility = Visibility.Collapsed;
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();
            PositionCollapsedKeepAlongEdge();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Toolbar] Collapse error: {ex.Message}");
            _isExpanded = false;
            ToolbarPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Re-snap to collapsed position but preserve the along-edge offset
    /// (so dragged position is remembered).
    /// When <paramref name="workAreaOverride"/> is provided, uses that work area instead of the primary.
    /// </summary>
    private void PositionCollapsedKeepAlongEdge(Rect? workAreaOverride = null)
    {
        // Ensure no stale nub margin persists — collapsed window IS the nub
        NubHandle.Margin = new Thickness(0);

        var workArea = workAreaOverride ?? GetWorkAreaForNub();
        switch (_anchorEdge)
        {
            case AnchorEdge.Right:
                Left = workArea.Right - NubWidth;
                Top = _nubScreenPos;
                break;
            case AnchorEdge.Left:
                Left = workArea.Left;
                Top = _nubScreenPos;
                break;
            case AnchorEdge.Top:
                Left = _nubScreenPos;
                Top = workArea.Top;
                break;
        }
    }

    /// <summary>
    /// Returns the work area of the monitor the nub is currently on,
    /// using 2D point matching via MonitorHelper.GetMonitorForNub to
    /// correctly disambiguate monitors with overlapping edge-axis ranges.
    /// </summary>
    private Rect GetWorkAreaForNub()
    {
        var monitors = MonitorHelper.GetAllMonitors();
        var monitor = MonitorHelper.GetMonitorForNub(_nubScreenPos, _anchorEdge, Left, Top, monitors);
        return monitor.WorkArea;
    }

    // ── Mouse Events ─────────────────────────────────────────────────

    private void NubHandle_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_inputHook?.IsDragInProgress == true) return;
        if (!_isExpanded && !_isDragging)
            ExpandToolbar();
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer?.Stop();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isExpanded && !_isDragging)
        {
            _collapseTimer?.Stop();
            _collapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CollapseDelayMs)
            };
            _collapseTimer.Tick += (_, _) =>
            {
                _collapseTimer.Stop();
                CollapseToolbar();
                // If we're still in fullscreen mode, fade back out after collapsing
                if (_hiddenDueToFullscreen)
                {
                    _expandedOverFullscreen = false;
                    FadeOpacity(1.0, 0.0);
                }
            };
            _collapseTimer.Start();
        }
    }

    // ── Nub Drag to Reposition ───────────────────────────────────────

    private void NubHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        e.Handled = true;

        // Record drag start for click detection (use current screen position)
        var relPos = e.GetPosition(this);
        _dragStartScreen = new Point(Left + relPos.X, Top + relPos.Y);

        // Collapse toolbar immediately when starting a drag
        _collapseTimer?.Stop();
        if (_isExpanded)
        {
            _isExpanded = false;
            BeginAnimation(Window.LeftProperty, null);
            BeginAnimation(Window.TopProperty, null);
            ToolbarPanel.Visibility = Visibility.Collapsed;
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();
            PositionCollapsedKeepAlongEdge();
        }

        // Always capture on the nub — it survives collapse (drag zones are inside ToolbarPanel)
        NubHandle.CaptureMouse();
    }

    private void NubHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var relPos = e.GetPosition(this);
        double mouseX = Left + relPos.X;
        double mouseY = Top + relPos.Y;
        var cursorPoint = new Point(mouseX, mouseY);

        // Get the work area and full bounds of the monitor under the cursor
        var workArea = MonitorHelper.GetWorkAreaForPoint(cursorPoint);
        var monitorBounds = MonitorHelper.GetMonitorBoundsForPoint(cursorPoint);
        double screenTop = monitorBounds.Top;
        double screenLeft = monitorBounds.Left;
        double screenBottom = monitorBounds.Bottom;
        double screenRight = monitorBounds.Right;

        // Determine which edge the cursor is closest to
        var nearestEdge = DetectClosestEdge(cursorPoint, workArea);

        // If edge changed during drag, reconfigure layout
        if (nearestEdge != _anchorEdge)
        {
            ConfigureLayout(nearestEdge);
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();
        }

        BeginAnimation(Window.LeftProperty, null);
        BeginAnimation(Window.TopProperty, null);

        // Center the nub on the cursor along the edge axis, clamped to monitor bounds
        switch (nearestEdge)
        {
            case AnchorEdge.Left:
                Left = workArea.Left;
                Top = Math.Clamp(mouseY - NubLength / 2, screenTop, screenBottom - NubLength);
                break;
            case AnchorEdge.Right:
                Left = workArea.Right - NubWidth;
                Top = Math.Clamp(mouseY - NubLength / 2, screenTop, screenBottom - NubLength);
                break;
            case AnchorEdge.Top:
                Left = Math.Clamp(mouseX - NubLength / 2, screenLeft, screenRight - NubLength);
                Top = workArea.Top;
                break;
        }

        _nubScreenPos = (nearestEdge == AnchorEdge.Top) ? Left : Top;
        _nubOffset = 0;
    }

    /// <summary>
    /// Determines which screen edge the point is closest to.
    /// </summary>
    private static AnchorEdge DetectClosestEdge(Point screenPoint, Rect workArea)
    {
        double distLeft = screenPoint.X - workArea.Left;
        double distRight = workArea.Right - screenPoint.X;
        double distTop = screenPoint.Y - workArea.Top;
        // No bottom edge support

        double minDist = Math.Min(distLeft, Math.Min(distRight, distTop));

        if (minDist == distTop) return AnchorEdge.Top;
        if (minDist == distLeft) return AnchorEdge.Left;
        return AnchorEdge.Right;
    }

    private void NubHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        NubHandle.ReleaseMouseCapture();

        var relPos = e.GetPosition(this);
        var endScreen = new Point(Left + relPos.X, Top + relPos.Y);
        double dist = Math.Abs(endScreen.X - _dragStartScreen.X) +
                      Math.Abs(endScreen.Y - _dragStartScreen.Y);

        // If barely moved, treat as click → expand
        if (dist < 5 && !_isExpanded)
        {
            PositionCollapsedKeepAlongEdge();
            ExpandToolbar();
            return;
        }

        var workArea = MonitorHelper.GetWorkAreaForPoint(endScreen);
        var newEdge = DetectClosestEdge(endScreen, workArea);

        // Note: _anchorEdge may already equal newEdge because NubHandle_MouseMove
        // calls ConfigureLayout during drag. Always sync ToolbarAnchorEdge here.
        if (_settings.ToolbarAnchorEdge != _anchorEdge)
            _settings.ToolbarAnchorEdge = _anchorEdge;

        if (newEdge != _anchorEdge)
        {
            if (_isExpanded)
            {
                _isExpanded = false;
                ToolbarPanel.Visibility = Visibility.Collapsed;
                BeginAnimation(Window.LeftProperty, null);
                BeginAnimation(Window.TopProperty, null);
            }

            _settings.ToolbarAnchorEdge = newEdge;
            UpdateAnchorEdge(newEdge);

            // Position on new edge at cursor (updates _nubScreenPos and _anchorEdge)
            SnapToEdgeAtCursor(newEdge, endScreen, workArea);
        }
        else
        {
            // Same edge — nub is already where it should be.
            // Just compute _nubOffset for the next expand.
            ComputeNubOffsetForExpand();
        }

        // Persist nub position (covers both same-edge and edge-changed paths)
        SaveNubPosition();
    }

    /// <summary>
    /// Snaps the collapsed nub to the given edge at the cursor position.
    /// </summary>
    private void SnapToEdgeAtCursor(AnchorEdge edge, Point cursor, Rect workArea)
    {
        if (_isExpanded)
        {
            _isExpanded = false;
            ToolbarPanel.Visibility = Visibility.Collapsed;
            BeginAnimation(Window.LeftProperty, null);
            BeginAnimation(Window.TopProperty, null);
        }

        SizeToContent = SizeToContent.WidthAndHeight;
        UpdateLayout();

        switch (edge)
        {
            case AnchorEdge.Left:
                Left = workArea.Left;
                Top = Math.Clamp(cursor.Y, workArea.Top, workArea.Bottom - NubLength);
                _nubScreenPos = Top;
                break;
            case AnchorEdge.Right:
                Left = workArea.Right - NubWidth;
                Top = Math.Clamp(cursor.Y, workArea.Top, workArea.Bottom - NubLength);
                _nubScreenPos = Top;
                break;
            case AnchorEdge.Top:
                Left = Math.Clamp(cursor.X, workArea.Left, workArea.Right - NubLength);
                Top = workArea.Top;
                _nubScreenPos = Left;
                break;
        }

        _nubOffset = 0;
        ComputeNubOffsetForExpand();
    }

    /// <summary>
    /// Computes and persists NubFraction, NubAnchorEdge, and NubMonitorFingerprint
    /// from the current _nubScreenPos and _anchorEdge, then calls Save().
    /// </summary>
    private void SaveNubPosition()
    {
        var monitors = MonitorHelper.GetAllMonitors();

        // Find the monitor whose work area contains the nub position using 2D point matching
        // to correctly disambiguate monitors with overlapping edge-axis ranges.
        var monitor = MonitorHelper.GetMonitorForNub(_nubScreenPos, _anchorEdge, Left, Top, monitors);

        double edgeStart, edgeLength;
        if (_anchorEdge == AnchorEdge.Top)
        {
            edgeStart = monitor.WorkArea.Left;
            edgeLength = monitor.WorkArea.Width;
        }
        else
        {
            edgeStart = monitor.WorkArea.Top;
            edgeLength = monitor.WorkArea.Height;
        }

        double fraction = edgeLength > NubLength
            ? Math.Clamp((_nubScreenPos - edgeStart) / (edgeLength - NubLength), 0.0, 1.0)
            : 0.5;

        _settings.NubFraction = fraction;
        _settings.NubAnchorEdge = _anchorEdge;
        _settings.NubMonitorFingerprint = MonitorHelper.BuildFingerprint(monitor);
        DebugLog.Write($"[Toolbar] SaveNubPosition: edge={_anchorEdge} fraction={fraction:F4} pos={_nubScreenPos:F1} fingerprint={_settings.NubMonitorFingerprint} workArea={monitor.WorkArea}");
        _settings.Save();
    }

    /// <summary>
    /// Computes _nubOffset from the current _nubScreenPos so the next expand
    /// positions the toolbar correctly around the nub.
    /// </summary>
    private void ComputeNubOffsetForExpand()
    {
        var workArea = GetWorkAreaForNub();

        // Re-measure toolbar to get correct dimensions for current orientation
        var wasVisible = ToolbarPanel.Visibility;
        ToolbarPanel.Visibility = Visibility.Visible;
        ToolbarPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _toolbarWidth = ToolbarPanel.DesiredSize.Width;
        _toolbarHeight = ToolbarPanel.DesiredSize.Height;
        ToolbarPanel.Visibility = wasVisible;

        double workAreaStart, workAreaEnd, faceLength;

        switch (_anchorEdge)
        {
            case AnchorEdge.Left:
            case AnchorEdge.Right:
                workAreaStart = workArea.Top;
                workAreaEnd = workArea.Bottom;
                faceLength = _toolbarHeight;
                break;
            default:
                workAreaStart = workArea.Left;
                workAreaEnd = workArea.Right;
                faceLength = _toolbarWidth;
                break;
        }

        // Where the toolbar would go if centered on the nub
        double toolbarPos = _nubScreenPos - faceLength / 2 + NubLength / 2;
        toolbarPos = Math.Clamp(toolbarPos, workAreaStart, workAreaEnd - faceLength);
        _nubOffset = _nubScreenPos - toolbarPos;
    }

    // ── Button Click Handlers ────────────────────────────────────────

    private void SpotlightButton_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(ToolType.Spotlight);
    }

    private void ArrowButton_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(ToolType.Arrow);
    }

    private void BoxButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(ToolType.Box);

    private void HighlightButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(ToolType.Highlight);

    private void StepsButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(ToolType.Steps);

    /// <summary>
    /// Sets the active tool, updates the visual highlight, and raises ActiveToolChanged.
    /// </summary>
    private void SetActiveTool(ToolType tool)
    {
        if (ActiveTool == tool) return;

        ActiveTool = tool;
        HighlightActiveToolButton();
        ActiveToolChanged?.Invoke(this, tool);
    }

    /// <summary>
    /// Sets the active tool from an external caller (e.g. hotkey), without re-raising ActiveToolChanged.
    /// </summary>
    public void SetActiveToolExternal(ToolType tool)
    {
        if (ActiveTool == tool) return;
        ActiveTool = tool;
        HighlightActiveToolButton();
        ActiveToolChanged?.Invoke(this, tool);
    }

    /// <summary>
    /// Applies the highlight background to the active tool button and clears all others.
    /// </summary>
    private void HighlightActiveToolButton()
    {
        SpotlightButton.Background = ActiveTool == ToolType.Spotlight ? ActiveToolBrush : Brushes.Transparent;
        ArrowButton.Background = ActiveTool == ToolType.Arrow ? ActiveToolBrush : Brushes.Transparent;
        BoxButton.Background = ActiveTool == ToolType.Box ? ActiveToolBrush : Brushes.Transparent;
        HighlightButton.Background = ActiveTool == ToolType.Highlight ? ActiveToolBrush : Brushes.Transparent;
        StepsButton.Background = ActiveTool == ToolType.Steps ? ActiveToolBrush : Brushes.Transparent;
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var original = btn.Background;
            btn.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                btn.Background = original;
            };
            timer.Start();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowSingleton(_settings);
    }

    private void DismissToolbarButton_Click(object sender, RoutedEventArgs e)
    {
        DismissToolbarRequested?.Invoke(this, EventArgs.Empty);
    }

    private static readonly (int Seconds, string Label)[] DismissDurations =
    {
        (60,    "for 1 minute"),
        (120,   "for 2 minutes"),
        (300,   "for 5 minutes"),
        (600,   "for 10 minutes"),
        (1800,  "for 30 minutes"),
        (3600,  "for 1 hour"),
        (7200,  "for 2 hours"),
        (10800, "for 3 hours"),
        (-1,    "until Settings is opened"),
    };

    private void UpdateDismissTooltip()
    {
        int duration = _settings.DismissToolbarDuration;
        string label = "Dismiss toolbar";
        foreach (var (seconds, text) in DismissDurations)
        {
            if (seconds == duration) { label = $"Dismiss toolbar {text}"; break; }
        }
        DismissToolbarButton.ToolTip = label;
    }

    // ── Settings Changed ─────────────────────────────────────────────

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            DebugLog.Write($"[Toolbar] OnSettingsChanged: _anchorEdge={_anchorEdge} settings.ToolbarAnchorEdge={_settings.ToolbarAnchorEdge} settings.NubAnchorEdge={_settings.NubAnchorEdge}");
            if (_anchorEdge != _settings.ToolbarAnchorEdge)
            {
                DebugLog.Write($"[Toolbar] OnSettingsChanged: calling UpdateAnchorEdge({_settings.ToolbarAnchorEdge})");
                UpdateAnchorEdge(_settings.ToolbarAnchorEdge);
            }

            if (_lastVisibility != _settings.FlyoutToolbarVisible)
            {
                _lastVisibility = _settings.FlyoutToolbarVisible;
                if (_settings.FlyoutToolbarVisible) ShowToolbar();
                else HideToolbar();
            }

            ApplyToolOrder();
            UpdateDismissTooltip();
        });
    }

    /// <summary>
    /// Reorders and shows/hides tool buttons in ButtonPanel to match ToolOrder setting.
    /// Settings and DismissToolbar pseudo-buttons snap to top or bottom.
    /// </summary>
    private void ApplyToolOrder()
    {
        var activeTools = SettingsService.ParseActiveToolOrder(_settings.ToolOrder);
        bool settingsPresent = _settings.ToolOrder.Contains("Settings", StringComparison.OrdinalIgnoreCase);
        bool settingsAtTop = _settings.ToolOrder.StartsWith("Settings", StringComparison.OrdinalIgnoreCase);
        bool dismissPresent = _settings.ToolOrder.Contains("DismissToolbar", StringComparison.OrdinalIgnoreCase);
        bool dismissAtTop = false;
        if (dismissPresent)
        {
            var parts = _settings.ToolOrder.Split(',');
            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (trimmed.Equals("DismissToolbar", StringComparison.OrdinalIgnoreCase)) { dismissAtTop = true; break; }
                if (trimmed.Equals("Settings", StringComparison.OrdinalIgnoreCase)) continue;
                break;
            }
        }

        var buttonMap = new System.Collections.Generic.Dictionary<ToolType, Button>
        {
            { ToolType.Spotlight, SpotlightButton },
            { ToolType.Arrow,     ArrowButton },
            { ToolType.Box,       BoxButton },
            { ToolType.Highlight, HighlightButton },
            { ToolType.Steps,     StepsButton },
        };

        // Remove all tool buttons, separators, and pseudo-tool buttons from panel
        foreach (var btn in buttonMap.Values)
            ButtonPanel.Children.Remove(btn);
        ButtonPanel.Children.Remove(Separator);
        ButtonPanel.Children.Remove(SettingsButton);
        ButtonPanel.Children.Remove(DismissToolbarButton);
        // Remove any dynamically-added dismiss separator
        if (_dismissSeparator != null)
        {
            ButtonPanel.Children.Remove(_dismissSeparator);
            _dismissSeparator = null;
        }

        // Determine which pseudo-tools go at top vs bottom
        bool anyPseudoAtTop = (settingsPresent && settingsAtTop) || (dismissPresent && dismissAtTop);
        bool anyPseudoAtBottom = (settingsPresent && !settingsAtTop) || (dismissPresent && !dismissAtTop);

        // Insert top pseudo-tools after DragZoneStart (index 1)
        int insertAt = 1;
        if (dismissPresent && dismissAtTop)
        {
            DismissToolbarButton.Visibility = Visibility.Visible;
            ButtonPanel.Children.Insert(insertAt++, DismissToolbarButton);
        }
        if (settingsPresent && settingsAtTop)
        {
            SettingsButton.Visibility = Visibility.Visible;
            ButtonPanel.Children.Insert(insertAt++, SettingsButton);
        }
        if (anyPseudoAtTop)
        {
            Separator.Visibility = Visibility.Visible;
            ButtonPanel.Children.Insert(insertAt++, Separator);
        }

        // Insert tool buttons
        foreach (var tool in activeTools)
        {
            buttonMap[tool].Visibility = Visibility.Visible;
            ButtonPanel.Children.Insert(insertAt++, buttonMap[tool]);
        }

        // Insert bottom pseudo-tools before DragZoneEnd
        if (anyPseudoAtBottom)
        {
            if (anyPseudoAtTop)
            {
                // Need a second separator for bottom
                _dismissSeparator = new System.Windows.Controls.Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(8, 4, 8, 4),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                };
                int endIdx = ButtonPanel.Children.Count - 1; // before DragZoneEnd
                ButtonPanel.Children.Insert(endIdx, _dismissSeparator);
            }
            else
            {
                Separator.Visibility = Visibility.Visible;
                int endIdx = ButtonPanel.Children.Count - 1;
                ButtonPanel.Children.Insert(endIdx, Separator);
            }

            int bottomIdx = ButtonPanel.Children.Count - 1; // before DragZoneEnd
            if (settingsPresent && !settingsAtTop)
            {
                SettingsButton.Visibility = Visibility.Visible;
                ButtonPanel.Children.Insert(bottomIdx, SettingsButton);
                bottomIdx++;
            }
            if (dismissPresent && !dismissAtTop)
            {
                DismissToolbarButton.Visibility = Visibility.Visible;
                ButtonPanel.Children.Insert(bottomIdx, DismissToolbarButton);
            }
        }

        // Hide buttons not in active list
        foreach (var tool in buttonMap.Keys)
            if (!activeTools.Contains(tool))
                buttonMap[tool].Visibility = Visibility.Collapsed;

        if (!settingsPresent) SettingsButton.Visibility = Visibility.Collapsed;
        if (!dismissPresent) DismissToolbarButton.Visibility = Visibility.Collapsed;
        if (!anyPseudoAtTop && !anyPseudoAtBottom) Separator.Visibility = Visibility.Collapsed;
    }

    private System.Windows.Controls.Border? _dismissSeparator;

    // ── Fullscreen Detection ─────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND    = 0x0003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT      = 0x0000;

    private void StartFullscreenDetection()
    {
        // Keep delegate alive — GC will collect it otherwise and crash
        _winEventDelegate = OnWinEvent;

        // Fires when any window becomes foreground (user switches to fullscreen app)
        _hookForeground = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Fires when any window moves or resizes (browser entering fullscreen mid-session)
        _hookLocationChange = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Fallback safety-net timer — catches anything the hooks miss (e.g. some games)
        _fullscreenCheckTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _fullscreenCheckTimer.Tick += (_, _) => CheckFullscreenState();
        _fullscreenCheckTimer.Start();

        // Clean up hooks when the window closes
        Closed += (_, _) => StopFullscreenDetection();
    }

    private void StopFullscreenDetection()
    {
        _fullscreenCheckTimer?.Stop();
        if (_hookForeground != IntPtr.Zero) { UnhookWinEvent(_hookForeground); _hookForeground = IntPtr.Zero; }
        if (_hookLocationChange != IntPtr.Zero) { UnhookWinEvent(_hookLocationChange); _hookLocationChange = IntPtr.Zero; }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // WINEVENT_OUTOFCONTEXT delivers callbacks on the thread that called SetWinEventHook,
        // which is the UI thread — so Dispatcher.BeginInvoke is not needed.
        // However we debounce location-change events: they fire hundreds of times per second
        // during a window resize/animation, so we only act after a short quiet period.
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (_debounceTimer == null)
            {
                _debounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); CheckFullscreenState(); };
            }
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
        else
        {
            // Foreground change — act immediately
            CheckFullscreenState();
        }
    }

    private void CheckFullscreenState()
    {
        try
        {
            var nubMonitor = GetNubMonitor();
            if (nubMonitor == null)
            {
                RestoreFromFullscreen();
                return;
            }

            bool foundFullscreen = FullscreenDetector.IsAnyFullscreenWindowOnMonitor(nubMonitor);

            if (foundFullscreen)
            {
                // Don't re-hide if the user is actively hovering the nub or using the toolbar
                if (!_expandedOverFullscreen)
                    HideForFullscreen();
            }
            else
            {
                RestoreFromFullscreen();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Toolbar] Fullscreen detection error: {ex.Message}");
        }
    }

    private MonitorInfo? GetNubMonitor()
    {
        var monitors = MonitorHelper.GetAllMonitors();
        return MonitorHelper.GetMonitorForNub(_nubScreenPos, _anchorEdge, Left, Top, monitors);
    }

    private void HideForFullscreen()
    {
        if (_hiddenDueToFullscreen || Visibility != Visibility.Visible) return;
        _hiddenDueToFullscreen = true;
        _expandedOverFullscreen = false;
        DebugLog.Write("[Toolbar] Hidden due to fullscreen window");
        FadeOpacity(Opacity, 0.0);
    }

    private void RestoreFromFullscreen()
    {
        if (!_hiddenDueToFullscreen) return;
        _hiddenDueToFullscreen = false;
        _expandedOverFullscreen = false;
        if (!_settings.FlyoutToolbarVisible) return;
        DebugLog.Write("[Toolbar] Restored from fullscreen hide");
        FadeOpacity(Opacity, 1.0);
    }

    /// <summary>
    /// Renders the spotlight icon using the same pipeline as the actual renderer:
    /// a white rectangle with a feathered rectangular cutout (transparent center),
    /// using CombinedGeometry + BlurEffect on a RenderTargetBitmap.
    /// </summary>
    internal static System.Windows.Media.Imaging.BitmapSource BuildSpotlightIconBitmap(
        int w, int h, int featherRadius)
    {
        // The cutout is centered, leaving a featherRadius-wide border on each side
        var cutout = new Rect(featherRadius, featherRadius,
            w - featherRadius * 2, h - featherRadius * 2);

        // Draw just the inner cutout rect as white — the surrounding area stays transparent
        var innerGeo = new RectangleGeometry(cutout);
        var drawing = new GeometryDrawing(System.Windows.Media.Brushes.White, null, innerGeo);
        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(drawing);

        var drawingVisual = new System.Windows.Media.DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
            dc.DrawDrawing(drawingGroup);

        // Apply blur to feather the edges
        drawingVisual.Effect = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = featherRadius,
            KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
        };

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(drawingVisual);
        rtb.Freeze();
        return rtb;
    }
}
