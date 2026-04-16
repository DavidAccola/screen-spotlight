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

        // Add overscan padding equal to the feather radius so blur at screen edges
        // has room to spread without being clipped, preventing soft fades at borders.
        int overscan = featherRadius;
        int paddedW = (int)((fullW + overscan * 2) * scale);
        int paddedH = (int)((fullH + overscan * 2) * scale);
        int w = Math.Max(1, paddedW);
        int h = Math.Max(1, paddedH);
        int scaledFeather = Math.Max(1, (int)(featherRadius * scale));

        DebugLog.Write($"[Renderer] BuildFeatheredMask: full={fullW}x{fullH}, scaled={w}x{h}, feather={scaledFeather}, cutouts={_cutouts.Count}");

        // Scale cutout rects to match the padded bitmap (offset by overscan)
        double expand = featherRadius * 0.5;
        var scaledCutouts = new List<Rect>(_cutouts.Count);
        foreach (var c in _cutouts)
        {
            var expanded = new Rect(
                c.X - expand, c.Y - expand,
                c.Width + expand * 2, c.Height + expand * 2);
            scaledCutouts.Add(new Rect(
                (expanded.X + overscan) * scale,
                (expanded.Y + overscan) * scale,
                expanded.Width * scale,
                expanded.Height * scale));
        }

        var element = new MaskElement(scaledCutouts, w, h, _settings.NestedSpotlightMode, scaledFeather)
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

        // Crop the viewport back to the non-padded region so the overscan is hidden
        double ox = (overscan * scale) / w;
        double oy = (overscan * scale) / h;
        double vw = (fullW * scale) / w;
        double vh = (fullH * scale) / h;

        return new ImageBrush(rtb)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(ox, oy, vw, vh)
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
        private readonly Models.NestedSpotlightMode _nestedMode;
        private readonly int _featherRadius;

        public MaskElement(IReadOnlyList<Rect> cutouts, int w, int h, Models.NestedSpotlightMode nestedMode, int featherRadius)
        {
            _cutouts = cutouts;
            _w = w;
            _h = h;
            _nestedMode = nestedMode;
            _featherRadius = featherRadius;
        }

        protected override void OnRender(DrawingContext dc)
        {
            DebugLog.Write($"[MaskElement] OnRender: cutouts={_cutouts.Count}, size={_w}x{_h}, mode={_nestedMode}");

            // Step 1: Build base mask geometry - full rectangle with ALL cutouts excluded
            Geometry baseMask = new RectangleGeometry(new Rect(0, 0, _w, _h));
            foreach (var cutout in _cutouts)
            {
                baseMask = new CombinedGeometry(GeometryCombineMode.Exclude, baseMask, new RectangleGeometry(cutout));
            }
            
            // Draw the base mask in white (dark overlay with transparent cutouts)
            dc.DrawGeometry(Brushes.White, null, baseMask);
            DebugLog.Write($"[MaskElement] Step 1: Drew base mask with {_cutouts.Count} cutouts excluded");

            // Step 2: Handle nested spotlights based on mode
            if (_nestedMode == Models.NestedSpotlightMode.Darken)
            {
                RenderDarkenMode(dc);
            }
            else // Replace mode
            {
                RenderReplaceMode(dc);
            }
        }

        /// <summary>
        /// Darken mode: nested spotlight creates darkness layer in surrounding area (can be cut through by later cutouts)
        /// </summary>
        private void RenderDarkenMode(DrawingContext dc)
        {
            // Add darkness layers for nested overlays
            // A cutout is "nested" if it's fully contained within the UNION of all earlier cutouts
            // This treats overlapping cutouts as one combined spotlight region
            int donutCount = 0;
            for (int i = 0; i < _cutouts.Count; i++)
            {
                // Build the union of all cutouts created before this one
                Geometry unionOfEarlier = Geometry.Empty;
                for (int j = 0; j < i; j++)
                {
                    unionOfEarlier = new CombinedGeometry(GeometryCombineMode.Union, unionOfEarlier, new RectangleGeometry(_cutouts[j]));
                }
                
                // Check if this cutout is fully contained within the union
                if (!unionOfEarlier.IsEmpty() && IsFullyContainedInGeometry(unionOfEarlier, _cutouts[i]))
                {
                    // This cutout is nested within the combined earlier spotlight region
                    // Create darkness layer: union of earlier cutouts minus this cutout
                    Geometry donutGeom = unionOfEarlier;
                    donutGeom = new CombinedGeometry(GeometryCombineMode.Exclude, donutGeom, new RectangleGeometry(_cutouts[i]));
                    
                    // Exclude cutouts that come AFTER this one (they should cut through the darkness)
                    for (int k = i + 1; k < _cutouts.Count; k++)
                    {
                        donutGeom = new CombinedGeometry(GeometryCombineMode.Exclude, donutGeom, new RectangleGeometry(_cutouts[k]));
                        DebugLog.Write($"[MaskElement] Excluding later cutout {k} from donut (nested cutout={i})");
                    }
                    
                    // Draw semi-transparent white to add darkness layer
                    // Alpha 128 = 50% additional darkness
                    dc.DrawGeometry(new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(128, 0xFF, 0xFF, 0xFF)),
                        null, donutGeom);
                    donutCount++;
                    DebugLog.Write($"[MaskElement] Darken mode: Drew donut layer {donutCount} for nested cutout {i} (within union of 0-{i-1})");
                }
            }
            DebugLog.Write($"[MaskElement] Darken mode complete: {donutCount} donut layers");
        }

        /// <summary>
        /// Replace mode: nested spotlight replaces parent - parent area goes full darkness (except the nested cutout itself)
        /// </summary>
        private void RenderReplaceMode(DrawingContext dc)
        {
            // For each nested cutout, fill the union of earlier cutouts with full white (darkness)
            // but exclude the nested cutout itself and any later cutouts
            // Expand the parent geometry to cover the feathered edge
            int replaceCount = 0;
            for (int i = 0; i < _cutouts.Count; i++)
            {
                // Build the union of all cutouts created before this one
                Geometry unionOfEarlier = Geometry.Empty;
                for (int j = 0; j < i; j++)
                {
                    unionOfEarlier = new CombinedGeometry(GeometryCombineMode.Union, unionOfEarlier, new RectangleGeometry(_cutouts[j]));
                }
                
                // Check if this cutout is fully contained within the union
                if (!unionOfEarlier.IsEmpty() && IsFullyContainedInGeometry(unionOfEarlier, _cutouts[i]))
                {
                    // This cutout replaces the parent spotlight region
                    // Expand the union geometry to cover the feathered edge (add extra margin)
                    // The feather extends beyond the cutout edge, so we need to cover that area
                    var expandedUnion = unionOfEarlier;
                    if (_featherRadius > 0)
                    {
                        // Expand by the feather radius to ensure we cover the entire feathered edge
                        expandedUnion = unionOfEarlier.GetWidenedPathGeometry(new System.Windows.Media.Pen(Brushes.White, _featherRadius * 2));
                    }
                    
                    // Fill the expanded union with full white (darkness), but exclude this cutout and later ones
                    Geometry replaceGeom = expandedUnion;
                    
                    // Exclude this cutout and all later cutouts
                    for (int k = i; k < _cutouts.Count; k++)
                    {
                        replaceGeom = new CombinedGeometry(GeometryCombineMode.Exclude, replaceGeom, new RectangleGeometry(_cutouts[k]));
                    }
                    
                    // Draw full white to make the parent area fully dark
                    dc.DrawGeometry(Brushes.White, null, replaceGeom);
                    replaceCount++;
                    DebugLog.Write($"[MaskElement] Replace mode: Filled parent region for nested cutout {i} (replaced union of 0-{i-1})");
                }
            }
            DebugLog.Write($"[MaskElement] Replace mode complete: {replaceCount} replacements");
        }

        /// <summary>
        /// Returns true if inner is fully contained within outer (all edges inside).
        /// </summary>
        private static bool IsFullyContained(Rect outer, Rect inner)
        {
            return inner.Left >= outer.Left &&
                   inner.Right <= outer.Right &&
                   inner.Top >= outer.Top &&
                   inner.Bottom <= outer.Bottom;
        }

        /// <summary>
        /// Returns true if the rectangle is fully contained within the geometry.
        /// Uses hit testing to check if all four corners are inside the geometry.
        /// </summary>
        private static bool IsFullyContainedInGeometry(Geometry geometry, Rect rect)
        {
            // Check all four corners - if all are inside the geometry, the rect is fully contained
            var topLeft = new System.Windows.Point(rect.Left, rect.Top);
            var topRight = new System.Windows.Point(rect.Right, rect.Top);
            var bottomLeft = new System.Windows.Point(rect.Left, rect.Bottom);
            var bottomRight = new System.Windows.Point(rect.Right, rect.Bottom);
            
            return geometry.FillContains(topLeft) &&
                   geometry.FillContains(topRight) &&
                   geometry.FillContains(bottomLeft) &&
                   geometry.FillContains(bottomRight);
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
