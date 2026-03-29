using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using SpotlightOverlay.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace SpotlightOverlay.Rendering;

/// <summary>
/// Stores arrow annotation data and builds WPF Path geometries for rendering.
/// Each arrow is a line segment with an optional arrowhead at the end point,
/// plus a dark shadow outline for visibility on any background.
/// </summary>
public class ArrowRenderer
{
    // Geometry constants
    private const double LineStrokeWidth = 3.0;
    private const double ArrowheadLength = 16.0;
    private const double ArrowheadHalfAngleRad = 15.0 * Math.PI / 180.0; // 15° each side
    private const double ShadowOffset = 1.0;
    private const double MinDragDistance = 10.0;

    private static readonly Color ShadowColor = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);

    private readonly List<(Point Start, Point End)> _arrows = new();

    /// <summary>Number of stored arrows.</summary>
    public int ArrowCount => _arrows.Count;

    /// <summary>Read-only view of all stored arrow start/end pairs.</summary>
    public IReadOnlyList<(Point Start, Point End)> Arrows => _arrows.AsReadOnly();

    /// <summary>
    /// Adds an arrow if the drag distance meets the minimum threshold (10 DIP).
    /// </summary>
    public void AddArrow(Point start, Point end)
    {
        if (GetDistance(start, end) < MinDragDistance)
            return;
        _arrows.Add((start, end));
    }

    /// <summary>Removes all stored arrows.</summary>
    public void ClearArrows() => _arrows.Clear();

    /// <summary>
    /// Computes the angle in radians from start to end (measured from positive X axis).
    /// </summary>
    public static double ComputeAngle(Point start, Point end)
    {
        return Math.Atan2(end.Y - start.Y, end.X - start.X);
    }

    /// <summary>
    /// Builds a WPF Path element for the arrow with the given color and arrowhead style.
    /// The Path includes the line shaft and arrowhead geometry.
    /// Returns null if the drag distance is below the minimum threshold (10 DIP).
    /// </summary>
    public Path? BuildArrowPath(Point start, Point end, Color color, ArrowheadStyle style)
    {
        if (GetDistance(start, end) < MinDragDistance)
            return null;

        return BuildPathForPoints(start, end, color, style);
    }

    /// <summary>
    /// Builds a shadow Path for the arrow (1 DIP offset, #CC000000).
    /// Returns null if the drag distance is below the minimum threshold.
    /// </summary>
    public Path? BuildShadowPath(Point start, Point end, ArrowheadStyle style)
    {
        if (GetDistance(start, end) < MinDragDistance)
            return null;

        var shadowStart = new Point(start.X + ShadowOffset, start.Y + ShadowOffset);
        var shadowEnd = new Point(end.X + ShadowOffset, end.Y + ShadowOffset);
        return BuildPathForPoints(shadowStart, shadowEnd, ShadowColor, style);
    }

    private Path BuildPathForPoints(Point start, Point end, Color color, ArrowheadStyle style)
    {
        double angle = ComputeAngle(start, end);
        var brush = new SolidColorBrush(color);

        // Build line geometry (shaft)
        var lineGeometry = new LineGeometry(start, end);

        // Build arrowhead geometry
        var arrowheadGeometry = BuildArrowheadGeometry(end, angle, style);

        // Combine into geometry group
        var geometryGroup = new GeometryGroup();
        geometryGroup.Children.Add(lineGeometry);
        if (arrowheadGeometry != null && !arrowheadGeometry.IsEmpty())
        {
            geometryGroup.Children.Add(arrowheadGeometry);
        }

        var path = new Path
        {
            Data = geometryGroup,
            Stroke = brush,
            StrokeThickness = LineStrokeWidth,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        // FilledTriangle: fill the arrowhead with solid color
        // (Fill on GeometryGroup only affects closed figures — the LineGeometry has no interior)
        if (style == ArrowheadStyle.FilledTriangle)
        {
            path.Fill = brush;
        }

        return path;
    }

    /// <summary>
    /// Builds the arrowhead geometry at the given tip point.
    /// The angle is the shaft direction in radians (from start toward end).
    /// Returns null for ArrowheadStyle.None.
    /// </summary>
    public static PathGeometry? BuildArrowheadGeometry(Point tip, double angle, ArrowheadStyle style)
    {
        switch (style)
        {
            case ArrowheadStyle.FilledTriangle:
            case ArrowheadStyle.OpenTriangle:
                return BuildTriangleArrowhead(tip, angle);

            case ArrowheadStyle.SimpleLines:
                return BuildChevronArrowhead(tip, angle);

            case ArrowheadStyle.None:
            default:
                return null;
        }
    }

    /// <summary>
    /// Builds a triangular arrowhead (closed polygon) at the tip.
    /// Used for both FilledTriangle (caller sets Fill) and OpenTriangle (stroke only).
    /// </summary>
    private static PathGeometry BuildTriangleArrowhead(Point tip, double angle)
    {
        // Two base points of the triangle, offset back from the tip
        double backAngle = angle + Math.PI; // reverse direction
        var left = new Point(
            tip.X + ArrowheadLength * Math.Cos(backAngle + ArrowheadHalfAngleRad),
            tip.Y + ArrowheadLength * Math.Sin(backAngle + ArrowheadHalfAngleRad));
        var right = new Point(
            tip.X + ArrowheadLength * Math.Cos(backAngle - ArrowheadHalfAngleRad),
            tip.Y + ArrowheadLength * Math.Sin(backAngle - ArrowheadHalfAngleRad));

        var figure = new PathFigure
        {
            StartPoint = tip,
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment(left, true));
        figure.Segments.Add(new LineSegment(right, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Builds a chevron (>) arrowhead — two line segments from the tip backward.
    /// Not closed, not filled.
    /// </summary>
    private static PathGeometry BuildChevronArrowhead(Point tip, double angle)
    {
        double backAngle = angle + Math.PI;
        var left = new Point(
            tip.X + ArrowheadLength * Math.Cos(backAngle + ArrowheadHalfAngleRad),
            tip.Y + ArrowheadLength * Math.Sin(backAngle + ArrowheadHalfAngleRad));
        var right = new Point(
            tip.X + ArrowheadLength * Math.Cos(backAngle - ArrowheadHalfAngleRad),
            tip.Y + ArrowheadLength * Math.Sin(backAngle - ArrowheadHalfAngleRad));

        // Left arm: from left point to tip
        var leftArm = new PathFigure
        {
            StartPoint = left,
            IsClosed = false,
            IsFilled = false,
        };
        leftArm.Segments.Add(new LineSegment(tip, true));

        // Right arm: from tip to right point
        var rightArm = new PathFigure
        {
            StartPoint = tip,
            IsClosed = false,
            IsFilled = false,
        };
        rightArm.Segments.Add(new LineSegment(right, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(leftArm);
        geometry.Figures.Add(rightArm);
        return geometry;
    }

    private static double GetDistance(Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
