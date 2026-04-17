using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Windows;

public partial class FlyoutNotification : Window
{
    private static FlyoutNotification? _current;
    private DispatcherTimer? _dismissTimer;
    private bool _isToolName;
    private int _generation; // incremented on each ShowToolName call to invalidate stale callbacks
    private static double _toolNameWidth = 0; // computed once from longest tool name
    private static double _iconNameWidth = 0; // icon+name mode width (computed once)
    private static double _nameOnlyWidth = 0; // name-only mode width (computed once)
    private static double _iconOnlyWidth = 0; // icon-only mode width (computed once)
    private Rect _monitorWorkArea; // the work area this flyout is bound to

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    public FlyoutNotification()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        }
    }

    /// <summary>
    /// Returns the DIP work area of the monitor the cursor is currently on.
    /// </summary>
    private static Rect GetCursorMonitorWorkArea()
    {
        GetCursorPos(out POINT pt);
        double scale = GetCursorDpiScale(pt);
        return MonitorHelper.GetWorkAreaForPoint(
            new System.Windows.Point(pt.x / scale, pt.y / scale));
    }

    private static double GetCursorDpiScale(POINT pt)
    {
        return MonitorHelper.GetDpiScale(new System.Windows.Point(pt.x, pt.y));
    }

    /// <summary>
    /// Applies a clip rectangle so the flyout is never visible outside its target monitor.
    /// The clip is in window-local coordinates.
    /// </summary>
    private void ApplyMonitorClip()
    {
        // Clip region in window-local coordinates: the intersection of the flyout
        // with the monitor work area, translated to (0,0)-based window coords.
        double clipLeft = Math.Max(0, _monitorWorkArea.Left - Left);
        double clipTop = 0;
        double clipRight = Math.Min(ActualWidth, _monitorWorkArea.Right - Left);
        double clipBottom = ActualHeight;
        Clip = new RectangleGeometry(new Rect(clipLeft, clipTop, Math.Max(0, clipRight - clipLeft), clipBottom));
    }

    public static void Show(string message, int displayMs = 2500)
    {
        // Dismiss any existing flyout immediately
        if (_current != null)
        {
            _current._dismissTimer?.Stop();
            _current.Close();
            _current = null;
        }

        var flyout = new FlyoutNotification();
        flyout.MessageText.Text = message;
        flyout.Width = MeasureText(message);
        _current = flyout;

        // Use the monitor the cursor is on
        var workArea = GetCursorMonitorWorkArea();
        flyout._monitorWorkArea = workArea;
        flyout.Top = workArea.Top + 16;
        flyout.Left = workArea.Right; // start off-screen right of this monitor

        flyout.Show();
        flyout.UpdateLayout();
        flyout.ApplyMonitorClip();

        // Slide in from the right edge of the current monitor
        var slideIn = new DoubleAnimation
        {
            From = workArea.Right,
            To = workArea.Right - flyout.ActualWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        // Update clip as the window slides so it stays hidden outside the monitor
        EventHandler renderHandler = null!;
        renderHandler = (_, _) => flyout.ApplyMonitorClip();
        CompositionTarget.Rendering += renderHandler;

        slideIn.Completed += (_, _) =>
        {
            CompositionTarget.Rendering -= renderHandler;
            flyout.Clip = null; // fully visible now, no clip needed

            // Auto-dismiss after delay
            flyout._dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(displayMs)
            };
            flyout._dismissTimer.Tick += (_, _) =>
            {
                flyout._dismissTimer.Stop();
                flyout.SlideOut();
            };
            flyout._dismissTimer.Start();
        };

        flyout.BeginAnimation(LeftProperty, slideIn);
    }

    private void SlideOut()
    {
        var workArea = _monitorWorkArea;

        // Re-apply clip during slide-out so the flyout doesn't bleed onto adjacent monitors
        EventHandler renderHandler = null!;
        renderHandler = (_, _) => ApplyMonitorClip();
        CompositionTarget.Rendering += renderHandler;

        var slideOut = new DoubleAnimation
        {
            To = workArea.Right,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        slideOut.Completed += (_, _) =>
        {
            CompositionTarget.Rendering -= renderHandler;
            if (_current == this) _current = null;
            Close();
        };

        BeginAnimation(LeftProperty, slideOut);
    }

    /// <summary>
    /// Shows a brief tool-name/icon flyout based on the current display mode settings.
    /// Computes the correct content and width for the (showIcon, showName) combination,
    /// then delegates to the existing fade-in/reuse/fade-out animation logic.
    /// </summary>
    public static void ShowToolSwitch(ToolType tool, bool showIcon, bool showName, int displayMs = 1200)
    {
        // Suppressed: both flags off → no flyout (Req 5.1)
        if (!showIcon && !showName)
            return;

        // Lazily compute per-mode widths on first use
        if (_iconNameWidth == 0)
            MeasureModeWidths();

        ToolType? iconTool;
        string name;
        double width;

        if (showIcon && showName)
        {
            // Icon + Name mode (Req 2.1, 2.2)
            iconTool = tool;
            name = ToolDisplayName(tool);
            width = _iconNameWidth;
        }
        else if (showIcon)
        {
            // Icon-only mode (Req 3.1, 3.2)
            iconTool = tool;
            name = "";
            width = _iconOnlyWidth;
        }
        else
        {
            // Name-only mode (Req 4.1, 4.2)
            iconTool = null;
            name = ToolDisplayName(tool);
            width = _nameOnlyWidth;
        }

        ShowToolName(name, iconTool, displayMs, width);
    }

    /// <summary>
    /// Shows a brief tool-name label in the upper-right corner — no slide, just appear and fade out.
    /// Reuses the existing window on rapid switches to avoid any flash.
    /// All tool names display at the same width (sized to the longest name).
    /// </summary>
    public static void ShowToolName(string toolName, int displayMs = 1200)
        => ShowToolName(toolName, null, displayMs);

    /// <summary>
    /// Shows a brief tool-name label with an optional icon in the upper-right corner.
    /// </summary>
    public static void ShowToolName(string toolName, string? icon, int displayMs = 1200)
    {
        // Compute fixed width from longest tool name once
        if (_toolNameWidth == 0)
            _toolNameWidth = MeasureLongestToolName();

        ShowToolName(toolName, (ToolType?)null, displayMs, _toolNameWidth);
    }

    /// <summary>
    /// Core implementation: shows a tool-name/icon flyout at the specified width.
    /// Handles fade-in, reuse on rapid switches, and fade-out animation.
    /// </summary>
    private static void ShowToolName(string toolName, ToolType? iconTool, int displayMs, double modeWidth)
    {
        var workArea = GetCursorMonitorWorkArea();

        if (_current != null && _current._isToolName)
        {
            // Reuse: bump generation to invalidate any pending fade-in/fade-out callbacks
            _current._generation++;
            int gen = _current._generation;

            _current._dismissTimer?.Stop();
            _current.MessageText.Text = toolName;
            ApplyIcon(_current, iconTool);

            // Update width and position in case the display mode changed
            _current.Width = modeWidth;
            _current.Left = workArea.Right - modeWidth;

            // Cancel any in-progress opacity animation and snap to fully visible
            _current.BeginAnimation(OpacityProperty, null);
            _current.Opacity = 1;

            // Restart dismiss timer
            var target = _current;
            _current._dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(displayMs) };
            _current._dismissTimer.Tick += (_, _) =>
            {
                if (target._generation != gen) return; // superseded
                target._dismissTimer?.Stop();
                FadeOutAndClose(target, gen);
            };
            _current._dismissTimer.Start();
            return;
        }

        // Dismiss any existing non-tool-name flyout
        if (_current != null)
        {
            _current._dismissTimer?.Stop();
            _current.Close();
            _current = null;
        }

        var flyout = new FlyoutNotification();
        flyout._isToolName = true;
        flyout._generation = 0;
        flyout._monitorWorkArea = workArea;
        flyout.MessageText.Text = toolName;
        ApplyIcon(flyout, iconTool);
        flyout.Width = modeWidth;
        flyout.Opacity = 0;
        _current = flyout;

        flyout.Top = workArea.Top + 16;
        flyout.Left = workArea.Right - modeWidth;

        flyout.Show();

        int myGen = flyout._generation;

        // Fade in
        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(120))
        };
        fadeIn.Completed += (_, _) =>
        {
            if (flyout._generation != myGen) return; // superseded by a reuse call
            flyout._dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(displayMs) };
            flyout._dismissTimer.Tick += (_, _) =>
            {
                if (flyout._generation != myGen) return;
                flyout._dismissTimer?.Stop();
                FadeOutAndClose(flyout, myGen);
            };
            flyout._dismissTimer.Start();
        };
        flyout.BeginAnimation(OpacityProperty, fadeIn);
    }

    private static void ApplyIcon(FlyoutNotification flyout, ToolType? iconTool)
    {
        if (iconTool.HasValue)
        {
            flyout.IconContainer.Child = BuildToolIcon(iconTool.Value);
            flyout.IconContainer.Visibility = Visibility.Visible;
        }
        else
        {
            flyout.IconContainer.Child = null;
            flyout.IconContainer.Visibility = Visibility.Collapsed;
        }
    }

    private static void FadeOutAndClose(FlyoutNotification flyout, int gen)
    {
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300))
        };
        fadeOut.Completed += (_, _) =>
        {
            if (flyout._generation != gen) return;
            if (_current == flyout) _current = null;
            flyout.Close();
        };
        flyout.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Measures the pixel width needed to display the longest tool name at the notification's font size,
    /// plus padding, so all tool-name toasts are the same width.
    /// Includes space for an icon character so the width is stable regardless of icon visibility.
    /// </summary>
    private static double MeasureLongestToolName()
    {
        string[] names = ["Spotlight", "Highlighter", "Arrow", "Box", "Steps"];
        const double iconSlotWidth = 18 + 6; // 18px icon + 6px margin

        double maxTextWidth = 0;
        foreach (var name in names)
        {
            double w = MeasureText(name);
            if (w > maxTextWidth) maxTextWidth = w;
        }
        // Add icon width so the flyout is wide enough for icon+name
        return maxTextWidth + iconSlotWidth;
    }

    /// <summary>
    /// Computes the maximum flyout width for each display mode across all tool types.
    /// Called lazily on first use; compute-once is safe since the tool set is fixed at compile time.
    /// Sets _iconNameWidth, _nameOnlyWidth, and _iconOnlyWidth.
    /// </summary>
    private static void MeasureModeWidths()
    {
        const double iconSlotWidth = 18 + 6; // 18px icon + 6px margin

        double maxNameOnly = 0;
        string[] names = [ToolDisplayName(ToolType.Spotlight), ToolDisplayName(ToolType.Arrow),
                          ToolDisplayName(ToolType.Box), ToolDisplayName(ToolType.Highlight),
                          ToolDisplayName(ToolType.Steps)];

        foreach (var name in names)
        {
            double nameW = MeasureText(name);
            if (nameW > maxNameOnly) maxNameOnly = nameW;
        }

        _nameOnlyWidth = maxNameOnly;
        _iconNameWidth = maxNameOnly + iconSlotWidth;
        // Icon-only: icon slot + standard padding (28px padding + 2px border)
        _iconOnlyWidth = iconSlotWidth + 28 + 2;
    }

    /// <summary>
    /// Builds the graphical icon UIElement for a given tool type,
    /// matching the icons used in FlyoutToolbarWindow buttons and SettingsWindow.
    /// </summary>
    internal static UIElement BuildToolIcon(ToolType tool)
    {
        const double size = 18;
        switch (tool)
        {
            case ToolType.Spotlight:
            {
                var img = new System.Windows.Controls.Image
                {
                    Width = size, Height = size,
                    Source = FlyoutToolbarWindow.BuildSpotlightIconBitmap(16, 13, featherRadius: 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                return img;
            }
            case ToolType.Arrow:
                return new System.Windows.Controls.TextBlock
                {
                    Text = "\u279C", FontSize = size,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
            case ToolType.Box:
            {
                var vb = new System.Windows.Controls.Viewbox { Width = 15, Height = 15 };
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 16, Height = 13,
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1.5,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
                vb.Child = rect;
                return vb;
            }
            case ToolType.Highlight:
                return new System.Windows.Controls.TextBlock
                {
                    Text = "\U0001F58D", FontSize = 15,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
            case ToolType.Steps:
            {
                var vb = new System.Windows.Controls.Viewbox { Width = size, Height = size };
                var grid = new System.Windows.Controls.Grid { Width = 18, Height = 26 };
                var path = new System.Windows.Shapes.Path
                {
                    Fill = System.Windows.Media.Brushes.White,
                    Stroke = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse(
                        "M 2,8 A 7,7 0 1 1 16,8 Q 16,13.1 9,23.4 Q 2,13.1 2,8 Z")
                };
                var num = new System.Windows.Controls.TextBlock
                {
                    Text = "1", FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                    IsHitTestVisible = false
                };
                grid.Children.Add(path);
                grid.Children.Add(num);
                vb.Child = grid;
                return vb;
            }
            default:
                return new System.Windows.Controls.TextBlock { Width = size };
        }
    }

    /// <summary>
    /// Returns the human-readable display name for a given tool type.
    /// </summary>
    internal static string ToolDisplayName(ToolType tool) => tool switch
    {
        ToolType.Spotlight => "Spotlight",
        ToolType.Arrow     => "Arrow",
        ToolType.Box       => "Box",
        ToolType.Highlight => "Highlighter",
        ToolType.Steps     => "Steps",
        _                  => tool.ToString()
    };

    private static double MeasureText(string text)
    {
        var typeface = new System.Windows.Media.Typeface("Segoe UI");
        var ft = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            13,
            System.Windows.Media.Brushes.White,
            96.0);
        return Math.Ceiling(ft.Width) + 28 + 2; // 14px padding each side + 2 for border
    }
}
