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
    public int CutoutCount => _cutouts.Count;
    public IReadOnlyList<Rect> Cutouts => _cutouts.AsReadOnly();

    /// <summary>
    /// Builds a clip geometry using CombinedGeometry.Exclude to cut elliptical holes
    /// out of the full overlay rectangle. This is the standard WPF approach for
    /// creating transparent regions in an opaque element.
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
