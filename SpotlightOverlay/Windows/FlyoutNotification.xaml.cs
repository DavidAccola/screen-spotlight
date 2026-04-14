using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SpotlightOverlay.Windows;

public partial class FlyoutNotification : Window
{
    private static FlyoutNotification? _current;
    private DispatcherTimer? _dismissTimer;
    private bool _isToolName;
    private int _generation; // incremented on each ShowToolName call to invalidate stale callbacks
    private static double _toolNameWidth = 0; // computed once from longest tool name

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

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

        // Position off-screen to the right, top area
        var workArea = SystemParameters.WorkArea;
        flyout.Top = workArea.Top + 16;
        flyout.Left = workArea.Right; // start off-screen

        flyout.Show();
        flyout.UpdateLayout();

        // Slide in
        var slideIn = new DoubleAnimation
        {
            From = workArea.Right,
            To = workArea.Right - flyout.ActualWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        slideIn.Completed += (_, _) =>
        {
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
        var workArea = SystemParameters.WorkArea;
        var slideOut = new DoubleAnimation
        {
            To = workArea.Right,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        slideOut.Completed += (_, _) =>
        {
            if (_current == this) _current = null;
            Close();
        };

        BeginAnimation(LeftProperty, slideOut);
    }

    /// <summary>
    /// Shows a brief tool-name label in the upper-right corner — no slide, just appear and fade out.
    /// Reuses the existing window on rapid switches to avoid any flash.
    /// All tool names display at the same width (sized to the longest name).
    /// </summary>
    public static void ShowToolName(string toolName, int displayMs = 1200)
    {
        var workArea = SystemParameters.WorkArea;

        // Compute fixed width from longest tool name once
        if (_toolNameWidth == 0)
            _toolNameWidth = MeasureLongestToolName();

        if (_current != null && _current._isToolName)
        {
            // Reuse: bump generation to invalidate any pending fade-in/fade-out callbacks
            _current._generation++;
            int gen = _current._generation;

            _current._dismissTimer?.Stop();
            _current.MessageText.Text = toolName;

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
        flyout.MessageText.Text = toolName;
        flyout.Width = _toolNameWidth;
        flyout.Opacity = 0;
        _current = flyout;

        flyout.Top = workArea.Top + 16;
        flyout.Left = workArea.Right - _toolNameWidth;

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
    /// </summary>
    private static double MeasureLongestToolName()
    {
        string[] names = ["Spotlight", "Highlighter", "Arrow", "Box", "Steps"];
        double maxTextWidth = 0;
        foreach (var name in names)
        {
            double w = MeasureText(name);
            if (w > maxTextWidth) maxTextWidth = w;
        }
        return maxTextWidth;
    }

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
