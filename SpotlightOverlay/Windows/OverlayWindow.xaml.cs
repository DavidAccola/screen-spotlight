using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SpotlightOverlay.Windows;

/// <summary>
/// Full-screen, borderless, transparent, topmost overlay window that displays
/// the darkened layer and spotlight cutouts on a single monitor.
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow(Rect monitorBounds, double overlayOpacity, int featherRadius = 15)
    {
        InitializeComponent();

        Left = monitorBounds.Left;
        Top = monitorBounds.Top;
        Width = monitorBounds.Width;
        Height = monitorBounds.Height;

        _overlayOpacity = overlayOpacity;
        _featherRadius = featherRadius;

        // Start with transparent background — will be faded in on first cutout
        _overlayBrush = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
        OverlayBorder.Background = _overlayBrush;
        InitNamedElements();

        // Start fully transparent to avoid flash during window creation
        Opacity = 0;
        Loaded += (_, _) =>
        {
            // In freeze mode, don't enable click-through — the frozen screenshot
            // should block clicks from reaching the desktop underneath.
            if (!WillHaveFrozenBackground)
                SetClickThrough(true);
            Opacity = 1;
        };
    }

    private double _overlayOpacity;
    private int _featherRadius;
    private SolidColorBrush _overlayBrush;
    private bool _hasFadedIn;

    /// <summary>
    /// Set to true before Show() when freeze mode is active.
    /// Prevents click-through so clicks don't pass to the desktop under the frozen screenshot.
    /// </summary>
    public bool WillHaveFrozenBackground { get; set; }

    /// <summary>
    /// Sets a frozen screenshot as the background behind the dark overlay.
    /// Cutout holes in the overlay will reveal this frozen image instead of the live screen.
    /// Uses an ImageBrush with Stretch=Fill so the physical-pixel bitmap maps correctly
    /// to the DIP-sized Border regardless of DPI scaling.
    /// </summary>
    public void SetFrozenBackground(System.Windows.Media.Imaging.BitmapSource screenshot)
    {
        DebugLog.Write($"[Overlay] SetFrozenBackground: bitmap={screenshot.PixelWidth}x{screenshot.PixelHeight} dpi={screenshot.DpiX}x{screenshot.DpiY}");
        DebugLog.Write($"[Overlay] Window: Width={Width} Height={Height} ActualWidth={ActualWidth} ActualHeight={ActualHeight}");

        // Set the screenshot as the window's own background so that the semi-transparent
        // OverlayBorder composites against the screenshot rather than the live desktop.
        // With AllowsTransparency=True, elements composite against the window surface,
        // not against sibling elements in the z-order.
        Background = new ImageBrush(screenshot)
        {
            Stretch = Stretch.Fill
        };
        FrozenBackground.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Animates the overlay background from transparent to the target opacity.
    /// Only runs once (first cutout batch). Returns true if first fade-in.
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
    /// Applies a clip geometry to the overlay border. The BlurEffect on the border
    /// automatically feathers the clip edges via GPU — no bitmap rendering needed.
    /// </summary>
    public void ApplyClipGeometry(Geometry clipGeometry)
    {
        OverlayBorder.Clip = clipGeometry;
    }

    /// <summary>
    /// Applies a feathered opacity mask (kept for compatibility).
    /// </summary>
    public void ApplyFeatheredMask(System.Windows.Media.Brush mask)
    {
        OverlayBorder.Clip = null;
        OverlayBorder.OpacityMask = mask;
    }

    /// <summary>
    /// Legacy: Applies a DrawingGroup-based opacity mask (kept for test compatibility).
    /// </summary>
    public void ApplyOpacityMask(DrawingGroup mask)
    {
        OverlayBorder.OpacityMask = new DrawingBrush(mask)
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

    public void BeginFadeOut(Action onComplete)
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

    #region Drag Preview

    private const double CornerSize = 8;

    public void ShowDragPreview(Rect rect, Models.PreviewStyle style)
    {
        if (style == Models.PreviewStyle.Crosshair || style == Models.PreviewStyle.Corners)
        {
            DragPreview.Visibility = Visibility.Collapsed;
            OutlineShadow.Visibility = Visibility.Collapsed;
            OutlineFg.Visibility = Visibility.Collapsed;
            ShowCornerBrackets(rect, style == Models.PreviewStyle.Corners);
        }
        else
        {
            HideCornerBrackets();
            DragPreview.Visibility = Visibility.Collapsed;
            // Shadow outline
            System.Windows.Controls.Canvas.SetLeft(OutlineShadow, rect.X);
            System.Windows.Controls.Canvas.SetTop(OutlineShadow, rect.Y);
            OutlineShadow.Width = rect.Width;
            OutlineShadow.Height = rect.Height;
            OutlineShadow.Visibility = Visibility.Visible;
            // Foreground outline
            System.Windows.Controls.Canvas.SetLeft(OutlineFg, rect.X);
            System.Windows.Controls.Canvas.SetTop(OutlineFg, rect.Y);
            OutlineFg.Width = rect.Width;
            OutlineFg.Height = rect.Height;
            OutlineFg.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Shows L-shaped corner brackets. When oppositeOnly is true, only
    /// top-left (┌) and bottom-right (┘) are shown.
    /// </summary>
    private void ShowCornerBrackets(Rect rect, bool oppositeOnly = false)
    {
        double len = Math.Min(CornerSize, Math.Min(rect.Width / 2, rect.Height / 2));

        // Top-left and bottom-right always shown
        SetLine(TL_Hs, rect.Left, rect.Top, rect.Left + len, rect.Top);
        SetLine(TL_Vs, rect.Left, rect.Top, rect.Left, rect.Top + len);
        SetLine(BR_Hs, rect.Right - len, rect.Bottom, rect.Right, rect.Bottom);
        SetLine(BR_Vs, rect.Right, rect.Bottom - len, rect.Right, rect.Bottom);
        SetLine(TL_H, rect.Left, rect.Top, rect.Left + len, rect.Top);
        SetLine(TL_V, rect.Left, rect.Top, rect.Left, rect.Top + len);
        SetLine(BR_H, rect.Right - len, rect.Bottom, rect.Right, rect.Bottom);
        SetLine(BR_V, rect.Right, rect.Bottom - len, rect.Right, rect.Bottom);

        if (oppositeOnly)
        {
            // Hide TR and BL
            TR_Hs.Visibility = Visibility.Collapsed; TR_Vs.Visibility = Visibility.Collapsed;
            BL_Hs.Visibility = Visibility.Collapsed; BL_Vs.Visibility = Visibility.Collapsed;
            TR_H.Visibility = Visibility.Collapsed; TR_V.Visibility = Visibility.Collapsed;
            BL_H.Visibility = Visibility.Collapsed; BL_V.Visibility = Visibility.Collapsed;
        }
        else
        {
            SetLine(TR_Hs, rect.Right - len, rect.Top, rect.Right, rect.Top);
            SetLine(TR_Vs, rect.Right, rect.Top, rect.Right, rect.Top + len);
            SetLine(BL_Hs, rect.Left, rect.Bottom, rect.Left + len, rect.Bottom);
            SetLine(BL_Vs, rect.Left, rect.Bottom - len, rect.Left, rect.Bottom);
            SetLine(TR_H, rect.Right - len, rect.Top, rect.Right, rect.Top);
            SetLine(TR_V, rect.Right, rect.Top, rect.Right, rect.Top + len);
            SetLine(BL_H, rect.Left, rect.Bottom, rect.Left + len, rect.Bottom);
            SetLine(BL_V, rect.Left, rect.Bottom - len, rect.Left, rect.Bottom);
        }
    }

    private static void SetLine(System.Windows.Shapes.Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1; line.Y1 = y1;
        line.X2 = x2; line.Y2 = y2;
        line.Visibility = Visibility.Visible;
    }

    private void HideCornerBrackets()
    {
        TL_H.Visibility = Visibility.Collapsed; TL_V.Visibility = Visibility.Collapsed;
        TR_H.Visibility = Visibility.Collapsed; TR_V.Visibility = Visibility.Collapsed;
        BL_H.Visibility = Visibility.Collapsed; BL_V.Visibility = Visibility.Collapsed;
        BR_H.Visibility = Visibility.Collapsed; BR_V.Visibility = Visibility.Collapsed;
        TL_Hs.Visibility = Visibility.Collapsed; TL_Vs.Visibility = Visibility.Collapsed;
        TR_Hs.Visibility = Visibility.Collapsed; TR_Vs.Visibility = Visibility.Collapsed;
        BL_Hs.Visibility = Visibility.Collapsed; BL_Vs.Visibility = Visibility.Collapsed;
        BR_Hs.Visibility = Visibility.Collapsed; BR_Vs.Visibility = Visibility.Collapsed;
    }

    public void HideDragPreview()
    {
        DragPreview.Visibility = Visibility.Collapsed;
        OutlineShadow.Visibility = Visibility.Collapsed;
        OutlineFg.Visibility = Visibility.Collapsed;
        HideCornerBrackets();
    }

    public void FinalizeDragPreview(Rect rect, Models.PreviewStyle style)
    {
        if (style == Models.PreviewStyle.Crosshair || style == Models.PreviewStyle.Corners)
        {
            AddStaticCornerBrackets(rect, style == Models.PreviewStyle.Corners);
            HideCornerBrackets();
        }
        else
        {
            // Shadow outline
            var shadowBox = new System.Windows.Shapes.Rectangle
            {
                Width = rect.Width, Height = rect.Height,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0, 0, 0)),
                StrokeThickness = 2, IsHitTestVisible = false
            };
            System.Windows.Controls.Canvas.SetLeft(shadowBox, rect.X);
            System.Windows.Controls.Canvas.SetTop(shadowBox, rect.Y);
            PreviewCanvas.Children.Add(shadowBox);

            // Foreground outline
            var fgBox = new System.Windows.Shapes.Rectangle
            {
                Width = rect.Width, Height = rect.Height,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
            System.Windows.Controls.Canvas.SetLeft(fgBox, rect.X);
            System.Windows.Controls.Canvas.SetTop(fgBox, rect.Y);
            PreviewCanvas.Children.Add(fgBox);

            OutlineShadow.Visibility = Visibility.Collapsed;
            OutlineFg.Visibility = Visibility.Collapsed;
        }
    }

    private void AddStaticCornerBrackets(Rect rect, bool oppositeOnly = false)
    {
        double len = Math.Min(CornerSize, Math.Min(rect.Width / 2, rect.Height / 2));
        var shadow = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0, 0, 0));
        var fg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

        // Top-left
        AddStaticLine(shadow, 2, rect.Left, rect.Top, rect.Left + len, rect.Top);
        AddStaticLine(shadow, 2, rect.Left, rect.Top, rect.Left, rect.Top + len);
        AddStaticLine(fg, 1, rect.Left, rect.Top, rect.Left + len, rect.Top);
        AddStaticLine(fg, 1, rect.Left, rect.Top, rect.Left, rect.Top + len);

        // Bottom-right
        AddStaticLine(shadow, 2, rect.Right - len, rect.Bottom, rect.Right, rect.Bottom);
        AddStaticLine(shadow, 2, rect.Right, rect.Bottom - len, rect.Right, rect.Bottom);
        AddStaticLine(fg, 1, rect.Right - len, rect.Bottom, rect.Right, rect.Bottom);
        AddStaticLine(fg, 1, rect.Right, rect.Bottom - len, rect.Right, rect.Bottom);

        if (!oppositeOnly)
        {
            // Top-right
            AddStaticLine(shadow, 2, rect.Right - len, rect.Top, rect.Right, rect.Top);
            AddStaticLine(shadow, 2, rect.Right, rect.Top, rect.Right, rect.Top + len);
            AddStaticLine(fg, 1, rect.Right - len, rect.Top, rect.Right, rect.Top);
            AddStaticLine(fg, 1, rect.Right, rect.Top, rect.Right, rect.Top + len);

            // Bottom-left
            AddStaticLine(shadow, 2, rect.Left, rect.Bottom, rect.Left + len, rect.Bottom);
            AddStaticLine(shadow, 2, rect.Left, rect.Bottom - len, rect.Left, rect.Bottom);
            AddStaticLine(fg, 1, rect.Left, rect.Bottom, rect.Left + len, rect.Bottom);
            AddStaticLine(fg, 1, rect.Left, rect.Bottom - len, rect.Left, rect.Bottom);
        }
    }

    private void AddStaticLine(SolidColorBrush brush, double thickness, double x1, double y1, double x2, double y2)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false
        };
        PreviewCanvas.Children.Add(line);
    }

    private readonly HashSet<System.Windows.UIElement> _namedElements = new();

    private void InitNamedElements()
    {
        _namedElements.Add(DragPreview);
        _namedElements.Add(OutlineShadow);
        _namedElements.Add(OutlineFg);
        _namedElements.Add(TL_H); _namedElements.Add(TL_V);
        _namedElements.Add(TR_H); _namedElements.Add(TR_V);
        _namedElements.Add(BL_H); _namedElements.Add(BL_V);
        _namedElements.Add(BR_H); _namedElements.Add(BR_V);
        _namedElements.Add(TL_Hs); _namedElements.Add(TL_Vs);
        _namedElements.Add(TR_Hs); _namedElements.Add(TR_Vs);
        _namedElements.Add(BL_Hs); _namedElements.Add(BL_Vs);
        _namedElements.Add(BR_Hs); _namedElements.Add(BR_Vs);
    }

    public void ClearFinalizedPreviews()
    {
        for (int i = PreviewCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (!_namedElements.Contains(PreviewCanvas.Children[i]))
                PreviewCanvas.Children.RemoveAt(i);
        }
    }

    #endregion

    #region Arrow Rendering

    private FrameworkElement? _arrowPreview;

    /// <summary>
    /// Adds a finalized arrow element to the ArrowCanvas.
    /// </summary>
    public void AddArrowVisual(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        ArrowCanvas.Children.Add(element);
    }

    /// <summary>
    /// Shows a live preview arrow on the ArrowCanvas.
    /// Removes any previous preview first, then adds the new one.
    /// </summary>
    public void ShowArrowPreview(FrameworkElement element)
    {
        HideArrowPreview();
        element.IsHitTestVisible = false;
        _arrowPreview = element;
        ArrowCanvas.Children.Add(_arrowPreview);
    }

    /// <summary>
    /// Removes the current arrow preview from the ArrowCanvas.
    /// </summary>
    public void HideArrowPreview()
    {
        if (_arrowPreview != null)
        {
            ArrowCanvas.Children.Remove(_arrowPreview);
            _arrowPreview = null;
        }
    }

    /// <summary>
    /// Removes all arrow visuals (finalized and preview) from the ArrowCanvas.
    /// </summary>
    public void ClearArrows()
    {
        ArrowCanvas.Children.Clear();
        _arrowPreview = null;
    }

    #endregion

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

    /// <summary>
    /// Animates old cutouts "filling back in" — a feathered dark patch fades from
    /// transparent to opaque over each cutout rect, visually closing the hole.
    /// The patch is expanded and blurred to match the feathered cutout edges.
    /// Calls onComplete when all animations finish.
    /// </summary>
    public void AnimateCutoutsFadeOut(IReadOnlyList<Rect> cutoutRects, Action? onComplete, int durationMs = 400)
    {
        if (cutoutRects.Count == 0) { onComplete?.Invoke(); return; }

        int remaining = cutoutRects.Count;
        double expand = _featherRadius * 0.5;

        foreach (var cutoutRect in cutoutRects)
        {
            // Expand the patch so the blur straddles the original cutout edge,
            // matching how BuildFeatheredMask expands cutouts
            var expandedRect = new Rect(
                cutoutRect.X - expand - _featherRadius,
                cutoutRect.Y - expand - _featherRadius,
                cutoutRect.Width + (expand + _featherRadius) * 2,
                cutoutRect.Height + (expand + _featherRadius) * 2);

            var patch = new System.Windows.Shapes.Rectangle
            {
                Width = expandedRect.Width,
                Height = expandedRect.Height,
                Fill = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb((byte)(_overlayOpacity * 255), 0, 0, 0)),
                Opacity = 0,
                IsHitTestVisible = false
            };

            if (_featherRadius > 0)
            {
                patch.Effect = new BlurEffect
                {
                    Radius = _featherRadius,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                };
            }

            System.Windows.Controls.Canvas.SetLeft(patch, expandedRect.X);
            System.Windows.Controls.Canvas.SetTop(patch, expandedRect.Y);
            FadeCanvas.Children.Add(patch);

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeIn.Completed += (_, _) =>
            {
                FadeCanvas.Children.Remove(patch);
                if (Interlocked.Decrement(ref remaining) == 0)
                    onComplete?.Invoke();
            };

            patch.BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    #region Click-Through P/Invoke

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// Reinforces the topmost z-order via SetWindowPos. Call after Show()
    /// to push above other topmost windows where possible.
    /// </summary>
    public void ForceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Forces a window handle to the top of the topmost z-order without activating it.
    /// </summary>
    public static void ForceTopmostHwnd(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void SetClickThrough(bool enabled)
    {
        // Never allow click-through while a frozen background is active —
        // clicks should not pass through to the desktop underneath.
        if (enabled && WillHaveFrozenBackground)
            return;

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
