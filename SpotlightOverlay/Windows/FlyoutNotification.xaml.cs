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
        _current = flyout;

        // Position off-screen to the right, top area
        var workArea = SystemParameters.WorkArea;
        flyout.Top = workArea.Top + 16;
        flyout.Left = workArea.Right; // start off-screen

        flyout.Show();

        // Slide in
        var slideIn = new DoubleAnimation
        {
            From = workArea.Right,
            To = workArea.Right - flyout.Width,
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
}
