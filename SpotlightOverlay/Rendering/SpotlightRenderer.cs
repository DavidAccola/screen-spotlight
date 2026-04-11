using System.Windows;
using System.Windows.Media;
using SpotlightOverlay.Services;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace SpotlightOverlay.Rendering;

/// <summary>
/// Maintains a list of spotlight cutout rectangles and generates geometry
/// for creating transparent holes in the overlay where cutouts are placed.
/// </summary>
public class SpotlightRenderer
{
    private readonly List<Rect> _cutouts = new();
    private readonly SettingsService _settings;

    public SpotlightRenderer(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void AddCutout(Rect rect) => _cutouts.Add(rect);
    public void ClearCutouts() => _cutouts.Clear();
    public void RemoveLastCutout() { if (_cutouts.Count > 0) _cutouts.RemoveAt(_cutouts.Count - 1); }
    public int CutoutCount => _cutouts.Count;
    public IReadOnlyList<Rect> Cutouts => _cutouts.AsReadOnly();

    /// <summary>
    /// Builds a clip geometry using CombinedGeometry.Exclude to cut rectangular holes
    /// out of the full overlay rectangle. Used as fallback when feather radius is 0.
    /// </summary>
    public Geometry BuildClipGeometry(Size overlaySize)
    {
        Geometry result = new RectangleGeometry(new Rect(0, 0, overlaySize.Width, overlaySize.Height));

        foreach (var cutout in _cutouts)
        {
            var cutoutRect = new RectangleGeometry(cutout);
            result = new CombinedGeometry(GeometryCombineMode.Exclude, result, cutoutRect);
        }

        return result;
    }

    /// <summary>
    /// Builds a feathered opacity mask by rendering a white-on-black mask through
    /// a BlurEffect. Uses a FrameworkElement wrapper so the GPU-accelerated blur
    /// is captured by RenderTargetBitmap (DrawingVisual.Effect alone is not rendered).
    /// </summary>
    public System.Windows.Media.Brush BuildFeatheredMask(Size overlaySize)
    {
        int featherRadius = _settings.FeatherRadius;
        int fullW = (int)overlaySize.Width;
        int fullH = (int)overlaySize.Height;

        // Render at 1/4 resolution for speed — blur hides the lower resolution
        const double scale = 0.25;
        int w = Math.Max(1, (int)(fullW * scale));
        int h = Math.Max(1, (int)(fullH * scale));
        int scaledFeather = Math.Max(1, (int)(featherRadius * scale));

        DebugLog.Write($"[Renderer] BuildFeatheredMask: full={fullW}x{fullH}, scaled={w}x{h}, feather={scaledFeather}, cutouts={_cutouts.Count}");

        // Scale cutout rects to match the smaller bitmap
        // Expand each cutout by half the feather radius so the blur
        // straddles the original edge (50% into dark, 50% into cutout)
        double expand = featherRadius * 0.5;
        var scaledCutouts = new List<Rect>(_cutouts.Count);
        foreach (var c in _cutouts)
        {
            var expanded = new Rect(
                c.X - expand, c.Y - expand,
                c.Width + expand * 2, c.Height + expand * 2);
            scaledCutouts.Add(new Rect(
                expanded.X * scale, expanded.Y * scale,
                expanded.Width * scale, expanded.Height * scale));
        }

        var element = new MaskElement(scaledCutouts, w, h)
        {
            Width = w,
            Height = h
        };

        if (scaledFeather > 0)
        {
            element.Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = scaledFeather,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            };
        }

        element.Measure(new Size(w, h));
        element.Arrange(new Rect(0, 0, w, h));

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        return new ImageBrush(rtb)
        {
            Stretch = Stretch.Fill
        };
    }

    /// <summary>
    /// Lightweight FrameworkElement that draws the opacity mask.
    /// Uses CombinedGeometry.Exclude to create a white shape with transparent holes,
    /// so the alpha channel carries the cutout information for OpacityMask.
    /// Wrapping in a FrameworkElement allows BlurEffect to be captured by RenderTargetBitmap.
    /// </summary>
    private class MaskElement : FrameworkElement
    {
        private readonly IReadOnlyList<Rect> _cutouts;
        private readonly int _w, _h;

        public MaskElement(IReadOnlyList<Rect> cutouts, int w, int h)
        {
            _cutouts = cutouts;
            _w = w;
            _h = h;
        }

        protected override void OnRender(DrawingContext dc)
        {
            // Build geometry: full rectangle with cutout holes excluded
            Geometry mask = new RectangleGeometry(new Rect(0, 0, _w, _h));
            foreach (var cutout in _cutouts)
            {
                mask = new CombinedGeometry(GeometryCombineMode.Exclude, mask, new RectangleGeometry(cutout));
            }

            // Draw the holed geometry in white (alpha=FF).
            // Cutout areas have no geometry = no pixels = alpha=0.
            // BlurEffect will feather the alpha transition at the edges.
            dc.DrawGeometry(Brushes.White, null, mask);
        }
    }


    /// <summary>
    /// Legacy: Builds a DrawingGroup opacity mask. Kept for test compatibility.
    /// </summary>
    public DrawingGroup BuildOpacityMask(Size overlaySize)
    {
        var drawingGroup = new DrawingGroup();
        int featherRadius = _settings.FeatherRadius;

        var backgroundRect = new Rect(0, 0, overlaySize.Width, overlaySize.Height);
        var backgroundDrawing = new GeometryDrawing(
            Brushes.White, null, new RectangleGeometry(backgroundRect));
        drawingGroup.Children.Add(backgroundDrawing);

        foreach (var cutout in _cutouts)
        {
            var cutoutDrawing = BuildCutoutDrawing(cutout, featherRadius);
            drawingGroup.Children.Add(cutoutDrawing);
        }

        return drawingGroup;
    }

    private static GeometryDrawing BuildCutoutDrawing(Rect cutout, int featherRadius)
    {
        var expandedRect = new Rect(
            cutout.X - featherRadius, cutout.Y - featherRadius,
            cutout.Width + 2 * featherRadius, cutout.Height + 2 * featherRadius);

        var gradientBrush = new RadialGradientBrush();
        gradientBrush.MappingMode = BrushMappingMode.RelativeToBoundingBox;
        gradientBrush.Center = new Point(0.5, 0.5);
        gradientBrush.GradientOrigin = new Point(0.5, 0.5);
        gradientBrush.RadiusX = 0.5;
        gradientBrush.RadiusY = 0.5;

        double totalWidth = expandedRect.Width;
        double totalHeight = expandedRect.Height;
        double innerFractionX = totalWidth > 0 ? cutout.Width / totalWidth : 0;
        double innerFractionY = totalHeight > 0 ? cutout.Height / totalHeight : 0;
        double innerFraction = Math.Min(innerFractionX, innerFractionY);

        gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Colors.Black, innerFraction));
        gradientBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

        var geometry = new RectangleGeometry(expandedRect);
        return new GeometryDrawing(gradientBrush, null, geometry);
    }
}
