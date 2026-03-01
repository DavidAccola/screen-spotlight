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

        Left = monitorBounds.Left;
        Top = monitorBounds.Top;
        Width = monitorBounds.Width;
        Height = monitorBounds.Height;

        _overlayOpacity = overlayOpacity;

        // Start with transparent background — will be faded in on first cutout
        _overlayBrush = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
        OverlayGrid.Background = _overlayBrush;
    }

    private double _overlayOpacity;
    private SolidColorBrush _overlayBrush;
    private bool _hasFadedIn;

    /// <summary>
    /// Animates the overlay background from transparent to the target opacity over the given duration.
    /// Only runs once (first cutout). Returns true if this was the first fade-in, false if already faded in.
    /// </summary>
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

    #region Drag Preview

    /// <summary>
    /// Shows a live preview rectangle at the given position and size (in DIP coordinates).
    /// </summary>
    public void ShowDragPreview(Rect rect)
    {
        System.Windows.Controls.Canvas.SetLeft(DragPreview, rect.X);
        System.Windows.Controls.Canvas.SetTop(DragPreview, rect.Y);
        DragPreview.Width = rect.Width;
        DragPreview.Height = rect.Height;
        DragPreview.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hides the live drag preview rectangle.
    /// </summary>
    public void HideDragPreview()
    {
        DragPreview.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Finalizes the current drag preview as a static outline box on the canvas.
    /// The preview border is cloned and kept visible while the active preview is hidden.
    /// </summary>
    public void FinalizeDragPreview(Rect rect)
    {
        var box = new System.Windows.Shapes.Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        System.Windows.Controls.Canvas.SetLeft(box, rect.X);
        System.Windows.Controls.Canvas.SetTop(box, rect.Y);
        PreviewCanvas.Children.Add(box);
        DragPreview.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Removes all finalized preview boxes from the canvas.
    /// </summary>
    public void ClearFinalizedPreviews()
    {
        // Remove everything except the DragPreview border
        for (int i = PreviewCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (PreviewCanvas.Children[i] != DragPreview)
                PreviewCanvas.Children.RemoveAt(i);
        }
    }

    #endregion

    /// <summary>
    /// Animates a fade-in effect for a newly added cutout. Places a temporary dark
    /// rectangle over the cutout area and fades it out, revealing the transparent hole.
    /// </summary>
    public void AnimateCutoutFadeIn(Rect cutoutRect)
    {
        var patch = new System.Windows.Shapes.Rectangle
        {
            Width = cutoutRect.Width,
            Height = cutoutRect.Height,
            Fill = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(_overlayOpacity * 255), 0, 0, 0)),
            IsHitTestVisible = false
        };

        System.Windows.Controls.Canvas.SetLeft(patch, cutoutRect.X);
        System.Windows.Controls.Canvas.SetTop(patch, cutoutRect.Y);
        FadeCanvas.Children.Add(patch);

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(500)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeOut.Completed += (_, _) => FadeCanvas.Children.Remove(patch);
        patch.BeginAnimation(OpacityProperty, fadeOut);
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
