using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SpotlightOverlay.Models;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SpotlightOverlay.Rendering;

/// <summary>
/// Options controlling how a step marker is rendered.
/// </summary>
public record StepsRenderOptions(
    StepsShape Shape,
    bool OutlineEnabled,
    double Size,
    System.Windows.Media.Color FillColor,
    System.Windows.Media.Color OutlineColor,
    string FontFamily,
    double FontSize,
    bool FontBold = false,
    System.Windows.Media.Color? FontColor = null
);

/// <summary>
/// Stores step annotation data and builds WPF visuals for step markers.
/// Each step is a numbered teardrop or circle anchored at a point with a directional tail.
/// </summary>
public class StepsRenderer
{
    private const double OutlineThickness = 2.0;
    public const double TailLengthFactor = 0.6;
    private const double TailBaseHalfWidth = 0.3;

    private readonly List<(Point Anchor, double TailAngle, int Number, StepsRenderOptions Options)> _steps = new();

    public int StepCount => _steps.Count;
    public int NextStepNumber => _steps.Count + 1;

    public void AddStep(Point anchorDip, double tailAngleRad, int stepNumber, StepsRenderOptions options)
        => _steps.Add((anchorDip, tailAngleRad, stepNumber, options));

    public void ClearSteps() => _steps.Clear();
    public void RemoveLastStep() { if (_steps.Count > 0) _steps.RemoveAt(_steps.Count - 1); }

    /// <summary>
    /// Builds a WPF Canvas containing the step marker shape and number label.
    /// Never returns null.
    /// </summary>
    public FrameworkElement BuildStepVisual(Point anchorDip, double tailAngleRad, int stepNumber, StepsRenderOptions options)
    {
        double size = options.Size;
        double outlineThickness = OutlineThickness;
        double tailLength = size * TailLengthFactor;

        // The outer canvas must be large enough to contain the shape regardless of tail direction.
        // Worst case: tail extends tailLength beyond the circle edge in any direction.
        // Add padding on all sides = tailLength + outline, so the circle center sits in the middle.
        double padding = tailLength + outlineThickness;
        double canvasSize = size + padding * 2;

        // The circle center within the canvas
        var center = new Point(canvasSize / 2, canvasSize / 2);

        Geometry geometry = options.Shape == StepsShape.Teardrop
            ? BuildTeardropGeometry(center, size, tailAngleRad)
            : BuildCircleGeometry(center, size);

        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(options.FillColor),
            Stroke = options.OutlineEnabled ? new SolidColorBrush(options.OutlineColor) : null,
            StrokeThickness = options.OutlineEnabled ? outlineThickness : 0.0,
            IsHitTestVisible = false
        };

        var fontColor = options.FontColor ?? System.Windows.Media.Colors.White;
        var textBlock = new TextBlock
        {
            Text = stepNumber.ToString(),
            FontFamily = new FontFamily(options.FontFamily),
            FontSize = options.FontSize,
            FontWeight = options.FontBold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(fontColor),
            IsHitTestVisible = false
        };
        // Match the lining figures used for measurement so rendered glyphs == measured glyphs.
        System.Windows.Documents.Typography.SetNumeralStyle(textBlock, FontNumeralStyle.Lining);

        // Use FormattedText.BuildGeometry() with lining figures for vector-level centering.
        //
        // Why FormattedText instead of GlyphTypeface.GetGlyphOutline directly:
        //   CharacterToGlyphMap gives the *default* glyph for a character, which for many
        //   OpenType fonts (Bickham Script Pro, Minion Pro, etc.) is an oldstyle figure —
        //   intentionally low-sitting with a non-centered visual mass. Lining figures sit
        //   on the baseline and reach cap-height, making them geometrically centerable.
        //   SetFontNumeralStyle(Lining) triggers the OpenType 'lnum' feature so WPF selects
        //   the correct glyph variant before we measure.
        //
        // Why BuildGeometry() instead of BuildHighlightGeometry():
        //   BuildHighlightGeometry returns the layout box (includes line-height padding).
        //   BuildGeometry returns the actual filled outline of the glyphs — the vector shape
        //   WPF will render, with no extra whitespace.
        var typeface = new Typeface(new FontFamily(options.FontFamily), FontStyles.Normal,
            options.FontBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

        var ft = new FormattedText(
            stepNumber.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface, options.FontSize, Brushes.White, 1.0);

        // BuildGeometry returns the vector outline of the glyphs at origin (0,0).
        // Its Bounds give the true visual extents — no hinting, no DPI, no metric philosophy.
        var glyphBounds = ft.BuildGeometry(new Point(0, 0)).Bounds;

        double textLeft, textTop;
        if (!glyphBounds.IsEmpty)
        {
            textLeft = center.X - (glyphBounds.X + glyphBounds.Width  / 2);
            textTop  = center.Y - (glyphBounds.Y + glyphBounds.Height / 2);
        }
        else
        {
            // Fallback for glyphs with no outline (e.g. space) — use layout box
            var layoutBounds = ft.BuildHighlightGeometry(new Point(0, 0)).Bounds;
            textLeft = center.X - (layoutBounds.X + layoutBounds.Width  / 2);
            textTop  = center.Y - (layoutBounds.Y + layoutBounds.Height / 2);
        }

        Canvas.SetLeft(textBlock, textLeft);
        Canvas.SetTop(textBlock, textTop);

        var outerCanvas = new Canvas
        {
            Width = canvasSize,
            Height = canvasSize,
            IsHitTestVisible = false
        };
        outerCanvas.Children.Add(path);
        outerCanvas.Children.Add(textBlock);

        // Position the canvas so the circle center sits at anchorDip
        Canvas.SetLeft(outerCanvas, anchorDip.X - center.X);
        Canvas.SetTop(outerCanvas, anchorDip.Y - center.Y);

        return outerCanvas;
    }

    /// <summary>
    /// Builds a map-pin teardrop as a single closed PathFigure:
    ///   touchPoint1 → ArcSegment (major arc, the circle body) → touchPoint2 → LineSegment → tip → LineSegment → touchPoint1
    ///
    /// The two tangent touch points are computed via acos(r/d) formula.
    /// Since the straight lines from tip to touchPoints are true tangents to the circle,
    /// the join is automatically smooth (G1 continuous) — no kink, no notch.
    /// No CombinedGeometry needed.
    /// </summary>
    private static Geometry BuildTeardropGeometry(Point center, double size, double tailAngleRad)
    {
        double radius = size / 2;
        double tailLength = size * TailLengthFactor;

        // Tail tip
        double tipDist = radius + tailLength;
        var tailTip = new Point(
            center.X + tipDist * Math.Cos(tailAngleRad),
            center.Y + tipDist * Math.Sin(tailAngleRad));

        // Touch points at the widest points of the circle: exactly ±90° from tail direction
        double th = Math.PI / 2;
        double dir = tailAngleRad;

        var touchPoint1 = new Point(
            center.X + radius * Math.Cos(dir + th),
            center.Y + radius * Math.Sin(dir + th));

        var touchPoint2 = new Point(
            center.X + radius * Math.Cos(dir - th),
            center.Y + radius * Math.Sin(dir - th));

        // Single PathFigure: start at touchPoint1, major arc to touchPoint2, line to tip, line back
        // The major arc sweeps the body of the circle (away from the tail direction).
        // th is the half-angle of the tail, so the arc spans 2*(π - th) which is > π → IsLargeArc = true.
        var figure = new PathFigure { StartPoint = touchPoint1, IsClosed = true };

        // Arc from touchPoint1 to touchPoint2 — the large arc (circle body)
        // SweepDirection: touchPoint1 is at dir+th, touchPoint2 is at dir-th.
        // Going clockwise from dir+th to dir-th sweeps the major arc (away from tail).
        figure.Segments.Add(new ArcSegment(
            point: touchPoint2,
            size: new Size(radius, radius),
            rotationAngle: 0,
            isLargeArc: true,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        // At ±90°, the circle tangent is parallel to the tail direction.
        // For G1 continuity, the bezier control point must lie along the tail direction from the touch point.
        // Placing it at 30% of the way toward the tip gives more bow near the circle, less near the tip.
        double bowFraction = 0.30;

        // touchPoint2 is at tailAngleRad + π/2. Circle tangent there points in tailAngleRad direction.
        // Control point: from touchPoint2, move in tailAngleRad direction by bowFraction * sideLength
        double side2len = Math.Sqrt(Math.Pow(tailTip.X - touchPoint2.X, 2) + Math.Pow(tailTip.Y - touchPoint2.Y, 2));
        var ctrl2 = new Point(
            touchPoint2.X + Math.Cos(tailAngleRad) * side2len * bowFraction,
            touchPoint2.Y + Math.Sin(tailAngleRad) * side2len * bowFraction);

        // touchPoint1 is at tailAngleRad - π/2. Circle tangent there points in tailAngleRad direction too.
        double side1len = Math.Sqrt(Math.Pow(tailTip.X - touchPoint1.X, 2) + Math.Pow(tailTip.Y - touchPoint1.Y, 2));
        var ctrl1 = new Point(
            touchPoint1.X + Math.Cos(tailAngleRad) * side1len * bowFraction,
            touchPoint1.Y + Math.Sin(tailAngleRad) * side1len * bowFraction);

        figure.Segments.Add(new QuadraticBezierSegment(ctrl2, tailTip, isStroked: true));
        figure.Segments.Add(new QuadraticBezierSegment(ctrl1, touchPoint1, isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Builds a circle geometry centered on center with diameter size.
    /// </summary>
    private static EllipseGeometry BuildCircleGeometry(Point center, double size)
        => new EllipseGeometry(center, size / 2, size / 2);
}
