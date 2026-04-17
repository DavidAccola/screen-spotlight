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
        // 1px shorter than the screen — prevents Windows from classifying this as a
        // fullscreen app, which would trigger Focus Assist / Do Not Disturb auto-activation
        // and cause the snooze bell icon to briefly appear in the taskbar.
        Height = monitorBounds.Height - 1;

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
            Viewbox = new Rect(0, 0, physW, physH - physMissing), 
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1)
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
    private FrameworkElement? _boxPreview;
    private readonly List<FrameworkElement> _boxVisuals = new();
    // Each arrow is stored as a group of visuals (shadow + main) so we can remove them together
    private readonly List<List<FrameworkElement>> _arrowVisualGroups = new();

    /// <summary>
    /// Adds a finalized arrow element to the ArrowCanvas.
    /// Call once per arrow group (shadow, then main) — groups are tracked for undo.
    /// </summary>
    public void AddArrowVisual(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        ArrowCanvas.Children.Add(element);
    }

    /// <summary>
    /// Marks the start of a new arrow group for undo tracking.
    /// Call before adding the shadow + main visuals for one arrow.
    /// </summary>
    public void BeginArrowGroup() => _arrowVisualGroups.Add(new List<FrameworkElement>());

    /// <summary>
    /// Adds a finalized arrow element to the current arrow group and the canvas.
    /// </summary>
    public void AddArrowVisualGrouped(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        if (_arrowVisualGroups.Count > 0)
            _arrowVisualGroups[^1].Add(element);
        ArrowCanvas.Children.Add(element);
    }

    /// <summary>
    /// Removes the most recently added arrow group (shadow + main) from the canvas.
    /// Returns true if an arrow was removed.
    /// </summary>
    public bool RemoveLastArrow()
    {
        if (_arrowVisualGroups.Count == 0) return false;
        var group = _arrowVisualGroups[^1];
        _arrowVisualGroups.RemoveAt(_arrowVisualGroups.Count - 1);
        foreach (var el in group)
            ArrowCanvas.Children.Remove(el);
        return true;
    }

    /// <summary>
    /// Fades out and removes the most recently added arrow group.
    /// </summary>
    public void AnimateRemoveLastArrow(int durationMs = 300)
    {
        if (_arrowVisualGroups.Count == 0) return;
        var group = _arrowVisualGroups[^1];
        _arrowVisualGroups.RemoveAt(_arrowVisualGroups.Count - 1);
        FadeOutAndRemove(group, ArrowCanvas, durationMs);
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
        _arrowVisualGroups.Clear();
        _arrowPreview = null;
    }

    /// <summary>
    /// Adds a finalized box element to the ArrowCanvas.
    /// </summary>
    public void AddBoxVisual(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        _boxVisuals.Add(element);
        ArrowCanvas.Children.Add(element);
    }

    /// <summary>
    /// Shows a live preview box on the ArrowCanvas.
    /// Removes any previous box preview first, then adds the new one.
    /// </summary>
    public void ShowBoxPreview(FrameworkElement element)
    {
        HideBoxPreview();
        element.IsHitTestVisible = false;
        _boxPreview = element;
        ArrowCanvas.Children.Add(_boxPreview);
    }

    /// <summary>
    /// Removes the current box preview from the ArrowCanvas.
    /// </summary>
    public void HideBoxPreview()
    {
        if (_boxPreview != null)
        {
            ArrowCanvas.Children.Remove(_boxPreview);
            _boxPreview = null;
        }
    }

    /// <summary>
    /// Removes all committed box visuals from the ArrowCanvas without affecting arrow visuals.
    /// </summary>
    public void ClearBoxes()
    {
        foreach (var visual in _boxVisuals)
            ArrowCanvas.Children.Remove(visual);
        _boxVisuals.Clear();
        HideBoxPreview();
    }

    /// <summary>
    /// Removes the most recently added box (shadow + main, 2 visuals) from the canvas.
    /// Returns true if a box was removed.
    /// </summary>
    public bool RemoveLastBox()
    {
        // Each box is 2 visuals: shadow then main (added in order)
        if (_boxVisuals.Count < 2) return false;
        for (int i = 0; i < 2; i++)
        {
            var el = _boxVisuals[^1];
            _boxVisuals.RemoveAt(_boxVisuals.Count - 1);
            ArrowCanvas.Children.Remove(el);
        }
        return true;
    }

    public void AnimateRemoveLastBox(int durationMs = 300)
    {
        if (_boxVisuals.Count < 2) return;
        var toRemove = new List<FrameworkElement> { _boxVisuals[^2], _boxVisuals[^1] };
        _boxVisuals.RemoveAt(_boxVisuals.Count - 1);
        _boxVisuals.RemoveAt(_boxVisuals.Count - 1);
        FadeOutAndRemove(toRemove, ArrowCanvas, durationMs);
    }

    #endregion

    #region Highlight Rendering

    private FrameworkElement? _highlightPreview;
    private readonly List<FrameworkElement> _highlightVisuals = new();

    /// <summary>
    /// Adds a finalized highlight element to the HighlightCanvas.
    /// </summary>
    public void AddHighlightVisual(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        _highlightVisuals.Add(element);
        HighlightCanvas.Children.Add(element);
    }

    /// <summary>
    /// Shows a live preview highlight on the HighlightCanvas.
    /// Removes any previous preview first, then adds the new one.
    /// </summary>
    public void ShowHighlightPreview(FrameworkElement element)
    {
        HideHighlightPreview();
        element.IsHitTestVisible = false;
        _highlightPreview = element;
        HighlightCanvas.Children.Add(_highlightPreview);
    }

    /// <summary>
    /// Removes the current highlight preview from the HighlightCanvas.
    /// </summary>
    public void HideHighlightPreview()
    {
        if (_highlightPreview != null)
        {
            HighlightCanvas.Children.Remove(_highlightPreview);
            _highlightPreview = null;
        }
    }

    /// <summary>
    /// Removes all committed highlight visuals from the HighlightCanvas.
    /// </summary>
    public void ClearHighlights()
    {
        foreach (var visual in _highlightVisuals)
            HighlightCanvas.Children.Remove(visual);
        _highlightVisuals.Clear();
        HideHighlightPreview();
    }

    /// <summary>
    /// Removes the most recently added highlight from the canvas.
    /// Returns true if a highlight was removed.
    /// </summary>
    public bool RemoveLastHighlight()
    {
        if (_highlightVisuals.Count == 0) return false;
        var el = _highlightVisuals[^1];
        _highlightVisuals.RemoveAt(_highlightVisuals.Count - 1);
        HighlightCanvas.Children.Remove(el);
        return true;
    }

    public void AnimateRemoveLastHighlight(int durationMs = 300)
    {
        if (_highlightVisuals.Count == 0) return;
        var el = _highlightVisuals[^1];
        _highlightVisuals.RemoveAt(_highlightVisuals.Count - 1);
        FadeOutAndRemove(new[] { el }, HighlightCanvas, durationMs);
    }

    /// <summary>
    /// Sets the opacity of the HighlightCanvas so all highlights composite as one flat layer.
    /// This is the key to non-overlap behavior: overlapping rects inside the canvas
    /// don't accumulate alpha — the whole canvas blends as a single unit.
    /// </summary>
    public void SetHighlightOpacity(double opacity)
    {
        HighlightCanvas.Opacity = Math.Clamp(opacity, 0.0, 1.0);
    }

    #endregion

    #region Steps Rendering

    private FrameworkElement? _stepsPreview;
    private readonly List<FrameworkElement> _stepsVisuals = new();

    /// <summary>
    /// Adds a finalized step element to the StepsCanvas.
    /// </summary>
    public void AddStepVisual(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        _stepsVisuals.Add(element);
        StepsCanvas.Children.Add(element);
    }

    /// <summary>
    /// Shows a live preview step on the StepsCanvas.
    /// Removes any previous preview first, then adds the new one.
    /// </summary>
    public void ShowStepsPreview(FrameworkElement element)
    {
        HideStepsPreview();
        element.IsHitTestVisible = false;
        _stepsPreview = element;
        StepsCanvas.Children.Add(_stepsPreview);
    }

    /// <summary>
    /// Removes the current steps preview from the StepsCanvas.
    /// </summary>
    public void HideStepsPreview()
    {
        if (_stepsPreview != null)
        {
            StepsCanvas.Children.Remove(_stepsPreview);
            _stepsPreview = null;
        }
    }

    /// <summary>
    /// Removes all committed step visuals from the StepsCanvas and hides the preview.
    /// Does NOT remove other tool visuals.
    /// </summary>
    public void ClearSteps()
    {
        foreach (var visual in _stepsVisuals)
            StepsCanvas.Children.Remove(visual);
        _stepsVisuals.Clear();
        HideStepsPreview();
    }

    /// <summary>
    /// Removes the most recently added step from the canvas.
    /// Returns true if a step was removed.
    /// </summary>
    public bool RemoveLastStep()
    {
        if (_stepsVisuals.Count == 0) return false;
        var el = _stepsVisuals[^1];
        _stepsVisuals.RemoveAt(_stepsVisuals.Count - 1);
        StepsCanvas.Children.Remove(el);
        return true;
    }

    public void AnimateRemoveLastStep(int durationMs = 300)
    {
        if (_stepsVisuals.Count == 0) return;
        var el = _stepsVisuals[^1];
        _stepsVisuals.RemoveAt(_stepsVisuals.Count - 1);
        FadeOutAndRemove(new[] { el }, StepsCanvas, durationMs);
    }

    private static void FadeOutAndRemove(IEnumerable<FrameworkElement> elements,
        System.Windows.Controls.Panel parent, int durationMs)
    {
        var list = elements.ToList();
        if (list.Count == 0) return;
        int remaining = list.Count;
        foreach (var el in list)
        {
            var fade = new DoubleAnimation
            {
                From = 1.0, To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) =>
            {
                parent.Children.Remove(el);
            };
            el.BeginAnimation(OpacityProperty, fade);
        }
    }

    #endregion

    /// <summary>
    /// Cross-fades from the current opacity mask to a new one over the given duration.
    /// Used for nested spotlight transitions where the mask changes structurally
    /// (donut/replace layers appear or disappear) and a simple patch animation won't work.
    /// 
    /// Technique: snapshot the current OverlayBorder state into a temporary Border on
    /// FadeCanvas, apply the new mask immediately to OverlayBorder, then fade out the
    /// snapshot. This ensures no traces of the old mask remain after the animation.
    /// </summary>

    /// <summary>
    /// Animates the donut-shaped darkness region between an outer spotlight and a
    /// nested inner spotlight. Only the donut area fades in — the rest of the screen
    /// is unaffected. Uses a feathered bitmap brush that matches the main mask's
    /// blur/feathering so edges are soft.
    /// </summary>
    public void AnimateDonutFadeIn(System.Windows.Media.Brush donutBrush, int durationMs = 400, Action? onComplete = null)
    {
        // The donut brush is an ImageBrush rendered with the same blur as the main mask.
        // It encodes the target darkness in its alpha channel (50% for darken, 100% for replace).
        // We use it as the OpacityMask of a full-screen black rectangle, then animate that
        // rectangle's Opacity from 0 to 1.
        var patch = new System.Windows.Shapes.Rectangle
        {
            Width = ActualWidth,
            Height = ActualHeight,
            Fill = _overlayBrush,  // same black brush as the overlay
            OpacityMask = donutBrush,
            IsHitTestVisible = false,
            Opacity = 0.0
        };

        System.Windows.Controls.Canvas.SetLeft(patch, 0);
        System.Windows.Controls.Canvas.SetTop(patch, 0);
        FadeCanvas.Children.Add(patch);

        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeIn.Completed += (_, _) =>
        {
            FadeCanvas.Children.Remove(patch);
            onComplete?.Invoke();
        };
        patch.BeginAnimation(OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Animates the donut-shaped darkness region fading OUT when a nested spotlight
    /// is removed (undo). Uses a feathered bitmap brush matching the main mask.
    /// </summary>
    public void AnimateDonutFadeOut(System.Windows.Media.Brush donutBrush, int durationMs = 300, Action? onComplete = null)
    {
        var patch = new System.Windows.Shapes.Rectangle
        {
            Width = ActualWidth,
            Height = ActualHeight,
            Fill = _overlayBrush,
            OpacityMask = donutBrush,
            IsHitTestVisible = false,
            Opacity = 1.0
        };

        System.Windows.Controls.Canvas.SetLeft(patch, 0);
        System.Windows.Controls.Canvas.SetTop(patch, 0);
        FadeCanvas.Children.Add(patch);

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeOut.Completed += (_, _) =>
        {
            FadeCanvas.Children.Remove(patch);
            onComplete?.Invoke();
        };
        patch.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void AnimateCutoutFadeIn(Rect cutoutRect, IReadOnlyList<Rect>? existingCutouts = null)
    {
        var patch = new System.Windows.Shapes.Path
        {
            IsHitTestVisible = false,
            Fill = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(_overlayOpacity * 255), 0, 0, 0))
        };

        // Build geometry: the new cutout rect minus any already-transparent existing cutouts
        Geometry patchGeometry = new RectangleGeometry(cutoutRect);
        if (existingCutouts != null)
        {
            foreach (var existing in existingCutouts)
            {
                // Only subtract if there's an actual overlap
                var intersection = Rect.Intersect(cutoutRect, existing);
                if (!intersection.IsEmpty)
                {
                    patchGeometry = new CombinedGeometry(
                        GeometryCombineMode.Exclude,
                        patchGeometry,
                        new RectangleGeometry(existing));
                }
            }
        }

        patch.Data = patchGeometry;
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
    /// remainingCutouts: cutouts that still exist after removal — overlap areas are excluded from the patch.
    /// Calls onComplete when all animations finish.
    /// </summary>
    public void AnimateCutoutsFadeOut(IReadOnlyList<Rect> cutoutRects, Action? onComplete,
        int durationMs = 400, IReadOnlyList<Rect>? remainingCutouts = null)
    {
        if (cutoutRects.Count == 0) { onComplete?.Invoke(); return; }

        int remaining = cutoutRects.Count;
        double expand = _featherRadius * 0.5;

        foreach (var cutoutRect in cutoutRects)
        {
            // Build the overlap exclusion geometry (remaining cutouts that intersect this one)
            Geometry? exclusionGeometry = null;
            if (remainingCutouts != null)
            {
                foreach (var other in remainingCutouts)
                {
                    var intersection = Rect.Intersect(cutoutRect, other);
                    if (!intersection.IsEmpty)
                    {
                        var intersectGeom = new RectangleGeometry(other);
                        exclusionGeometry = exclusionGeometry == null
                            ? (Geometry)intersectGeom
                            : new CombinedGeometry(GeometryCombineMode.Union, exclusionGeometry, intersectGeom);
                    }
                }
            }

            // Expand the patch so the blur straddles the original cutout edge
            var expandedRect = new Rect(
                cutoutRect.X - expand - _featherRadius,
                cutoutRect.Y - expand - _featherRadius,
                cutoutRect.Width + (expand + _featherRadius) * 2,
                cutoutRect.Height + (expand + _featherRadius) * 2);

            FrameworkElement patch;

            if (exclusionGeometry != null)
            {
                // Use a Path with clip geometry to exclude overlapping areas
                Geometry patchGeometry = new RectangleGeometry(expandedRect);
                patchGeometry = new CombinedGeometry(
                    GeometryCombineMode.Exclude, patchGeometry, exclusionGeometry);

                patch = new System.Windows.Shapes.Path
                {
                    Data = patchGeometry,
                    Fill = new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb((byte)(_overlayOpacity * 255), 0, 0, 0)),
                    Opacity = 0,
                    IsHitTestVisible = false
                };
                // Apply blur via clip on the canvas element
                if (_featherRadius > 0)
                {
                    patch.Effect = new BlurEffect
                    {
                        Radius = _featherRadius,
                        KernelType = KernelType.Gaussian,
                        RenderingBias = RenderingBias.Performance
                    };
                }
            }
            else
            {
                var rect = new System.Windows.Shapes.Rectangle
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
                    rect.Effect = new BlurEffect
                    {
                        Radius = _featherRadius,
                        KernelType = KernelType.Gaussian,
                        RenderingBias = RenderingBias.Performance
                    };
                }
                System.Windows.Controls.Canvas.SetLeft(rect, expandedRect.X);
                System.Windows.Controls.Canvas.SetTop(rect, expandedRect.Y);
                patch = rect;
            }

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
