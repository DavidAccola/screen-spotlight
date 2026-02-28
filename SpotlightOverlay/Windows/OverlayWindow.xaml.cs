using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SpotlightOverlay.Windows;

/// <summary>
/// Full-screen, borderless, transparent, topmost overlay window that displays
/// the darkened layer and spotlight cutouts on a single monitor.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// Creates an OverlayWindow positioned and sized to cover the specified monitor bounds
    /// with the given overlay opacity.
    /// </summary>
    /// <param name="monitorBounds">The full bounds of the target monitor in screen coordinates.</param>
    /// <param name="overlayOpacity">The opacity for the semi-transparent dark background (0.0–1.0).</param>
    public OverlayWindow(Rect monitorBounds, double overlayOpacity)
    {
        InitializeComponent();

        // Position and size the window to cover the full monitor bounds (Req 3.3, 7.2)
        Left = monitorBounds.Left;
        Top = monitorBounds.Top;
        Width = monitorBounds.Width;
        Height = monitorBounds.Height;

        // Store opacity for fade-out animation reference
        _overlayOpacity = overlayOpacity;

        // Set the semi-transparent dark background (Req 3.4)
        // Black regions in OpacityMask = show this background (dark overlay)
        // White regions in OpacityMask = hide this background (reveal desktop)
        OverlayGrid.Background = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb((byte)(overlayOpacity * 255), 0, 0, 0));
    }

    private double _overlayOpacity;

    /// <summary>
    /// Applies a clip geometry to the overlay grid, cutting out transparent holes
    /// where spotlight cutouts should reveal the desktop underneath.
    /// Uses CombinedGeometry.Exclude approach — the standard WPF technique.
    /// </summary>
    public void ApplyClipGeometry(Geometry clipGeometry)
    {
        OverlayGrid.Clip = clipGeometry;
    }

    /// <summary>
    /// Legacy: Applies a DrawingGroup-based opacity mask (kept for test compatibility).
    /// </summary>
    public void ApplyOpacityMask(DrawingGroup mask)
    {
        OverlayGrid.OpacityMask = new DrawingBrush(mask)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, Width, Height),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1)
        };
    }

    /// <summary>
    /// Begins a 300ms fade-out animation on the window, then closes it and invokes the callback.
    /// The callback is used by the caller (e.g., App.xaml.cs) to clear cutouts in the renderer.
    /// </summary>
    /// <param name="onComplete">Action invoked after the fade-out completes and the window is closed.</param>
    public void BeginFadeOut(Action onComplete)
    {
        var animation = new DoubleAnimation
        {
            From = Opacity,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300))
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));

        storyboard.Completed += (_, _) =>
        {
            Close();
            onComplete?.Invoke();
        };

        storyboard.Begin();
    }

    #region Click-Through P/Invoke (Req 3.5, 3.6)

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Toggles click-through mode on the overlay window.
    /// When enabled, mouse events pass through to underlying windows (Req 3.5).
    /// When disabled, the overlay captures mouse events for drag tracking (Req 3.6).
    /// </summary>
    /// <param name="enabled">True to enable click-through; false to capture mouse events.</param>
    public void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (enabled)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
    }

    #endregion
}
