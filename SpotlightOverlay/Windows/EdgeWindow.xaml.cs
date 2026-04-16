using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SpotlightOverlay.Windows;

public partial class EdgeWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private double _overlayOpacity;
    private SolidColorBrush _overlayBrush;
    private bool _hasFadedIn;

    public EdgeWindow(Rect monitorBounds, double overlayOpacity)
    {
        InitializeComponent();

        Left = monitorBounds.Left;
        Top = monitorBounds.Bottom - 1;
        Width = monitorBounds.Width;
        Height = 1;

        _overlayOpacity = overlayOpacity;
        _overlayBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
        OverlayBorder.Background = _overlayBrush;

        Opacity = 0;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED;
                exStyle &= ~WS_EX_APPWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            }
        };
        Loaded += (_, _) => Opacity = 1;
    }

    public void SetFrozenBackground(System.Windows.Media.Imaging.BitmapSource screenshot, Rect monitorBoundsDip)
    {
        double physScaleY = screenshot.PixelHeight / monitorBoundsDip.Height;
        double physW = screenshot.PixelWidth;
        double physH = screenshot.PixelHeight;
        double physMissing = 1 * physScaleY;

        Background = new ImageBrush(screenshot)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, physH - physMissing, physW, physMissing),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1)
        };
    }

    public bool FadeInBackground(int durationMs = 500)
    {
        if (_hasFadedIn) return false;
        _hasFadedIn = true;

        var targetColor = System.Windows.Media.Color.FromArgb((byte)(_overlayOpacity * 255), 0, 0, 0);
        var animation = new ColorAnimation
        {
            From = System.Windows.Media.Color.FromArgb(0, 0, 0, 0),
            To = targetColor,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        _overlayBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        return true;
    }

    public void BeginFadeOut(Action? onComplete = null)
    {
        var animation = new DoubleAnimation
        {
            From = Opacity,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300))
        };

        animation.Completed += (_, _) =>
        {
            Visibility = Visibility.Hidden;
            onComplete?.Invoke();
        };

        BeginAnimation(OpacityProperty, animation);
    }

    public void BeginFadeIn(int durationMs = 500)
    {
        Opacity = 0;
        Visibility = Visibility.Visible;

        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs))
        };

        BeginAnimation(OpacityProperty, animation);
    }
}
