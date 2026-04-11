using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using SpotlightOverlay.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace SpotlightOverlay.Rendering;

/// <summary>
/// Stores arrow annotation data and builds WPF Path geometries for rendering.
/// Supports independent left/right endpoint styles and line dash styles.
/// </summary>
public class ArrowRenderer
{
    private const double LineStrokeWidth = 3.0;
    private const double ArrowheadLength = 22.0;
    private const double ArrowheadHalfAngleRad = 27.5 * Math.PI / 180.0;
    private const double ShadowOffset = 1.0;
    private const double MinDragDistance = 10.0;

    private static readonly Color ShadowColor = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);

    private readonly List<(Point Start, Point End)> _arrows = new();
    public int ArrowCount => _arrows.Count;
    public IReadOnlyList<(Point Start, Point End)> Arrows => _arrows.AsReadOnly();

    public void AddArrow(Point start, Point end)
    {
        if (GetDistance(start, end) < MinDragDistance) return;
        _arrows.Add((start, end));
    }

    public void ClearArrows() => _arrows.Clear();
    public void RemoveLastArrow() { if (_arrows.Count > 0) _arrows.RemoveAt(_arrows.Count - 1); }

    public static double ComputeAngle(Point start, Point end) =>
        Math.Atan2(end.Y - start.Y, end.X - start.X);

    // ── Legacy single-style API (used by App.xaml.cs) ──────────────

    public FrameworkElement? BuildArrowPath(Point start, Point end, Color color, ArrowheadStyle style) =>
        BuildArrowPath(start, end, color, ArrowheadStyle.None, style, ArrowLineStyle.Solid);

    public FrameworkElement? BuildShadowPath(Point start, Point end, ArrowheadStyle style) =>
        BuildShadowPath(start, end, ArrowheadStyle.None, style, ArrowLineStyle.Solid);

    // ── Full API with left/right ends and line style ───────────────

    public FrameworkElement? BuildArrowPath(Point start, Point end, Color color,
        ArrowheadStyle leftEnd, ArrowheadStyle rightEnd, ArrowLineStyle lineStyle,
        double leftSize = 22, double lineThickness = 3, double rightSize = 22)
    {
        if (GetDistance(start, end) < MinDragDistance) return null;
        return BuildPathForPoints(start, end, color, leftEnd, rightEnd, lineStyle, leftSize, lineThickness, rightSize);
    }

    public FrameworkElement? BuildShadowPath(Point start, Point end,
        ArrowheadStyle leftEnd, ArrowheadStyle rightEnd, ArrowLineStyle lineStyle,
        double leftSize = 22, double lineThickness = 3, double rightSize = 22)
    {
        if (GetDistance(start, end) < MinDragDistance) return null;
        var s = new Point(start.X + ShadowOffset, start.Y + ShadowOffset);
        var e = new Point(end.X + ShadowOffset, end.Y + ShadowOffset);
        return BuildPathForPoints(s, e, ShadowColor, leftEnd, rightEnd, lineStyle, leftSize, lineThickness, rightSize);
    }

    private static FrameworkElement BuildPathForPoints(Point start, Point end, Color color,
        ArrowheadStyle leftEnd, ArrowheadStyle rightEnd, ArrowLineStyle lineStyle,
        double leftSize, double lineThickness, double rightSize)
    {
        double angle = ComputeAngle(start, end);
        var brush = new SolidColorBrush(color);

        // Pull back line endpoints so the shaft stops at the arrowhead base
        double leftPull = GetPullback(leftEnd, leftSize, lineThickness, lineStyle);
        double rightPull = GetPullback(rightEnd, rightSize, lineThickness, lineStyle);
        var lineStart = new Point(
            start.X + leftPull * Math.Cos(angle),
            start.Y + leftPull * Math.Sin(angle));
        var lineEnd = new Point(
            end.X - rightPull * Math.Cos(angle),
            end.Y - rightPull * Math.Sin(angle));

        // Shaft line (dashed/dotted applies here only)
        var shaftPath = new Path
        {
            Data = new LineGeometry(lineStart, lineEnd),
            Stroke = brush,
            StrokeThickness = lineThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        if (lineStyle == ArrowLineStyle.Dashed)
        {
            // Dashed: medium segments with equal gaps, scale down as thickness grows
            double scale = Math.Max(0.4, 2.5 / lineThickness);
            double dashLen = 5 * scale;
            double gapLen = 3 * scale;
            shaftPath.StrokeDashArray = new DoubleCollection { dashLen, gapLen };
            double lineLenInUnits = GetDistance(lineStart, lineEnd) / lineThickness;
            double cycle = dashLen + gapLen;
            double halfLineInCycles = lineLenInUnits / 2.0;
            double offsetNeeded = halfLineInCycles - dashLen / 2.0;
            double remainder = offsetNeeded % cycle;
            shaftPath.StrokeDashOffset = -remainder;
            shaftPath.StrokeStartLineCap = PenLineCap.Flat;
            shaftPath.StrokeEndLineCap = PenLineCap.Flat;
        }
        else if (lineStyle == ArrowLineStyle.Dotted)
        {
            // Dotted: small square segments, tighter spacing
            double scale = Math.Max(0.3, 1.5 / lineThickness);
            double dotLen = Math.Max(1.0, 1.0 * scale);
            double gapLen = dotLen;
            shaftPath.StrokeDashArray = new DoubleCollection { dotLen, gapLen };
            double lineLenInUnits = GetDistance(lineStart, lineEnd) / lineThickness;
            double cycle = dotLen + gapLen;
            double halfLineInCycles = lineLenInUnits / 2.0;
            double offsetNeeded = halfLineInCycles - dotLen / 2.0;
            double remainder = offsetNeeded % cycle;
            shaftPath.StrokeDashOffset = -remainder;
            shaftPath.StrokeDashCap = PenLineCap.Flat;
            shaftPath.StrokeStartLineCap = PenLineCap.Flat;
            shaftPath.StrokeEndLineCap = PenLineCap.Flat;
        }

        // Offset arrowhead tips outward to compensate for line thickness (per-style)
        double leftCapExt = GetCapExtension(leftEnd, lineThickness);
        double rightCapExt = GetCapExtension(rightEnd, lineThickness);
        var leftTip = new Point(
            start.X - leftCapExt * Math.Cos(angle),
            start.Y - leftCapExt * Math.Sin(angle));
        var rightTip = new Point(
            end.X + rightCapExt * Math.Cos(angle),
            end.Y + rightCapExt * Math.Sin(angle));

        // Arrowhead geometries — separate groups for filled vs chevron (different stroke)
        var headGroup = new GeometryGroup();    // filled arrowheads (fixed thin stroke)
        var chevronGroup = new GeometryGroup(); // open chevrons (match line thickness)
        bool needsFill = false;

        var leftGeom = BuildArrowheadGeometry(leftTip, angle + Math.PI, leftEnd, leftSize);
        if (leftGeom != null && !leftGeom.IsEmpty())
        {
            if (leftEnd == ArrowheadStyle.OpenArrowhead)
                chevronGroup.Children.Add(leftGeom);
            else
            {
                headGroup.Children.Add(leftGeom);
                if (NeedsFill(leftEnd)) needsFill = true;
            }
        }

        var rightGeom = BuildArrowheadGeometry(rightTip, angle, rightEnd, rightSize);
        if (rightGeom != null && !rightGeom.IsEmpty())
        {
            if (rightEnd == ArrowheadStyle.OpenArrowhead)
                chevronGroup.Children.Add(rightGeom);
            else
            {
                headGroup.Children.Add(rightGeom);
                if (NeedsFill(rightEnd)) needsFill = true;
            }
        }

        // If no arrowheads, just return the shaft
        if (headGroup.Children.Count == 0 && chevronGroup.Children.Count == 0)
            return shaftPath;

        // Combine shaft + arrowheads in a Canvas
        var canvas = new System.Windows.Controls.Canvas { IsHitTestVisible = false };
        shaftPath.IsHitTestVisible = false;
        canvas.Children.Add(shaftPath);

        // Filled/solid arrowheads (fixed thin stroke)
        if (headGroup.Children.Count > 0)
        {
            var headPath = new Path
            {
                Data = headGroup,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Bevel,
                IsHitTestVisible = false,
            };
            if (needsFill) headPath.Fill = brush;
            canvas.Children.Add(headPath);
        }

        // Open chevron arrowheads (match line thickness)
        if (chevronGroup.Children.Count > 0)
        {
            var chevronPath = new Path
            {
                Data = chevronGroup,
                Stroke = brush,
                StrokeThickness = lineThickness,
                StrokeLineJoin = PenLineJoin.Miter,
                StrokeMiterLimit = 20,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat,
                IsHitTestVisible = false,
            };
            canvas.Children.Add(chevronPath);
        }

        return canvas;
    }

    private static double GetCapExtension(ArrowheadStyle style, double lineThickness) => style switch
    {
        ArrowheadStyle.FilledTriangle => lineThickness * 0.7,
        ArrowheadStyle.Barbed => lineThickness * 0.9,
        ArrowheadStyle.OpenArrowhead => lineThickness * 0.9,
        ArrowheadStyle.DotEnd => lineThickness * 0.4,
        _ => 0,
    };

    private static bool NeedsFill(ArrowheadStyle style) =>
        style is ArrowheadStyle.FilledTriangle or ArrowheadStyle.Barbed or ArrowheadStyle.DotEnd;

    /// <summary>
    /// How far to pull back the line from the endpoint so it stops at the arrowhead base.
    /// </summary>
    private static double GetPullback(ArrowheadStyle style, double size, double lineThickness, ArrowLineStyle lineStyle)
    {
        double capExtension = lineThickness / 2.0;
        double geoPull = style switch
        {
            ArrowheadStyle.FilledTriangle => size * Math.Cos(ArrowheadHalfAngleRad),
            ArrowheadStyle.Barbed => size * 0.5,
            ArrowheadStyle.OpenArrowhead => 0,
            ArrowheadStyle.DotEnd => size * 0.25,
            _ => 0,
        };
        double pull = Math.Max(0, geoPull - capExtension);
        // For dashed/dotted, reduce pullback so the last segment sits flush against the arrowhead
        if (lineStyle != ArrowLineStyle.Solid && style != ArrowheadStyle.None)
            pull = Math.Max(0, pull - lineThickness * 0.5);
        return pull;
    }

    public static PathGeometry? BuildArrowheadGeometry(Point tip, double angle, ArrowheadStyle style, double size = 22)
    {
        return style switch
        {
            ArrowheadStyle.FilledTriangle => BuildTriangleArrowhead(tip, angle, size),
            ArrowheadStyle.OpenArrowhead => BuildChevronArrowhead(tip, angle, size),
            ArrowheadStyle.Barbed => BuildBarbedArrowhead(tip, angle, size),
            ArrowheadStyle.DotEnd => BuildDotEndGeometry(tip, size),
            _ => null,
        };
    }

    private static PathGeometry BuildTriangleArrowhead(Point tip, double angle, double size)
    {
        double back = angle + Math.PI;
        var left = new Point(tip.X + size * Math.Cos(back + ArrowheadHalfAngleRad),
                             tip.Y + size * Math.Sin(back + ArrowheadHalfAngleRad));
        var right = new Point(tip.X + size * Math.Cos(back - ArrowheadHalfAngleRad),
                              tip.Y + size * Math.Sin(back - ArrowheadHalfAngleRad));
        var fig = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(left, true));
        fig.Segments.Add(new LineSegment(right, true));
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    private static PathGeometry BuildChevronArrowhead(Point tip, double angle, double size)
    {
        double back = angle + Math.PI;
        var left = new Point(tip.X + size * Math.Cos(back + ArrowheadHalfAngleRad),
                             tip.Y + size * Math.Sin(back + ArrowheadHalfAngleRad));
        var right = new Point(tip.X + size * Math.Cos(back - ArrowheadHalfAngleRad),
                              tip.Y + size * Math.Sin(back - ArrowheadHalfAngleRad));
        // Single figure: left → tip → right, so the join at tip creates a sharp miter point
        var fig = new PathFigure { StartPoint = left, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new LineSegment(tip, true));
        fig.Segments.Add(new LineSegment(right, true));
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    private static PathGeometry BuildBarbedArrowhead(Point tip, double angle, double size)
    {
        double barbLength = size;
        double barbHalfAngle = 30.0 * Math.PI / 180.0; // 60° tip angle
        double back = angle + Math.PI;
        
        var left = new Point(tip.X + barbLength * Math.Cos(back + barbHalfAngle),
                             tip.Y + barbLength * Math.Sin(back + barbHalfAngle));
        var right = new Point(tip.X + barbLength * Math.Cos(back - barbHalfAngle),
                              tip.Y + barbLength * Math.Sin(back - barbHalfAngle));
        
        double notchDepth = barbLength * 0.5; // equilateral: notch at midpoint
        var notch = new Point(tip.X + notchDepth * Math.Cos(back),
                              tip.Y + notchDepth * Math.Sin(back));
        
        var fig = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(left, true));
        fig.Segments.Add(new LineSegment(notch, true));
        fig.Segments.Add(new LineSegment(right, true));
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    private static PathGeometry BuildDotEndGeometry(Point center, double size)
    {
        double radius = size * 0.25;
        var fig = new PathFigure { StartPoint = new Point(center.X - radius, center.Y), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new ArcSegment(new Point(center.X + radius, center.Y),
            new System.Windows.Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        fig.Segments.Add(new ArcSegment(new Point(center.X - radius, center.Y),
            new System.Windows.Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    private static double GetDistance(Point a, Point b)
    {
        double dx = b.X - a.X; double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
