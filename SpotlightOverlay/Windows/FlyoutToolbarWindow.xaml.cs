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

    private readonly SettingsService _settings;
    private AnchorEdge _anchorEdge;
    private bool _isExpanded;
    private bool _spotlightActive;
    private DispatcherTimer? _collapseTimer;
    private bool _lastVisibility;

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
        ArrowButton.Click += ToolButton_Click;
        NumbersButton.Click += ToolButton_Click;
        HighlightButton.Click += ToolButton_Click;
        BoxButton.Click += ToolButton_Click;
        SettingsButton.Click += SettingsButton_Click;

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

            PositionCollapsed();
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
    /// </summary>
    private void PositionCollapsed()
    {
        _nubOffset = 0;
        ToolbarPanel.Visibility = Visibility.Collapsed;
        _isExpanded = false;

        var workArea = SystemParameters.WorkArea;

        // Size the window to just the nub
        SizeToContent = SizeToContent.WidthAndHeight;
        UpdateLayout();

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

            var workArea = SystemParameters.WorkArea;

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
    /// </summary>
    private void PositionCollapsedKeepAlongEdge()
    {
        // Ensure no stale nub margin persists — collapsed window IS the nub
        NubHandle.Margin = new Thickness(0);

        var workArea = SystemParameters.WorkArea;
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

    // ── Mouse Events ─────────────────────────────────────────────────

    private void NubHandle_MouseEnter(object sender, MouseEventArgs e)
    {
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
        var workArea = SystemParameters.WorkArea;

        // Determine which edge the cursor is closest to
        var nearestEdge = DetectClosestEdge(new Point(mouseX, mouseY), workArea);

        // If edge changed during drag, reconfigure layout
        if (nearestEdge != _anchorEdge)
        {
            ConfigureLayout(nearestEdge);
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();
        }

        BeginAnimation(Window.LeftProperty, null);
        BeginAnimation(Window.TopProperty, null);

        // Dead simple: center the nub on the cursor along the edge axis
        switch (nearestEdge)
        {
            case AnchorEdge.Left:
                Left = workArea.Left;
                Top = Math.Clamp(mouseY - NubLength / 2, workArea.Top, workArea.Bottom - NubLength);
                break;
            case AnchorEdge.Right:
                Left = workArea.Right - NubWidth;
                Top = Math.Clamp(mouseY - NubLength / 2, workArea.Top, workArea.Bottom - NubLength);
                break;
            case AnchorEdge.Top:
                Left = Math.Clamp(mouseX - NubLength / 2, workArea.Left, workArea.Right - NubLength);
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

        var workArea = SystemParameters.WorkArea;
        var newEdge = DetectClosestEdge(endScreen, workArea);

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
            _settings.Save();
            UpdateAnchorEdge(newEdge);

            // Position on new edge at cursor
            SnapToEdgeAtCursor(newEdge, endScreen, workArea);
        }
        else
        {
            // Same edge — nub is already where it should be.
            // Just compute _nubOffset for the next expand.
            ComputeNubOffsetForExpand();
        }
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
    /// Computes _nubOffset from the current _nubScreenPos so the next expand
    /// positions the toolbar correctly around the nub.
    /// </summary>
    private void ComputeNubOffsetForExpand()
    {
        var workArea = SystemParameters.WorkArea;
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
        _spotlightActive = !_spotlightActive;
        SpotlightButton.Background = _spotlightActive
            ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
            : Brushes.Transparent;
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

    // ── Settings Changed ─────────────────────────────────────────────

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_anchorEdge != _settings.ToolbarAnchorEdge)
                UpdateAnchorEdge(_settings.ToolbarAnchorEdge);

            if (_lastVisibility != _settings.FlyoutToolbarVisible)
            {
                _lastVisibility = _settings.FlyoutToolbarVisible;
                if (_settings.FlyoutToolbarVisible) ShowToolbar();
                else HideToolbar();
            }
        });
    }
}
