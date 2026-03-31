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

    public static double ComputeAngle(Point start, Point end) =>
        Math.Atan2(end.Y - start.Y, end.X - start.X);

    // ── Legacy single-style API (used by App.xaml.cs) ──────────────

    public FrameworkElement? BuildArrowPath(Point start, Point end, Color color, ArrowheadStyle style) =>
        BuildArrowPath(start, end, color, ArrowheadStyle.None, style, ArrowLineStyle.Solid);

    public FrameworkElement? BuildShadowPath(Point start, Point end, ArrowheadStyle style) =>
        BuildShadowPath(start, end, ArrowheadStyle.None, style, ArrowLineStyle.Solid);

    // ── Full API with left/right ends and line style ───────────────

    public FrameworkElement? BuildArrowPath(Point start, Point end, Color color,
        ArrowheadStyle leftEnd, ArrowheadStyle rightEnd, ArrowLineStyle lineStyle)
    {
        if (GetDistance(start, end) < MinDragDistance) return null;
        return BuildPathForPoints(start, end, color, leftEnd, rightEnd, lineStyle);
    }

    public FrameworkElement? BuildShadowPath(Point start, Point end,
        ArrowheadStyle leftEnd, ArrowheadStyle rightEnd, ArrowLineStyle lineStyle)
    {
        if (GetDistance(start, end) < MinDragDistance) return null;
        var s = new Point(start.X + ShadowOffset, start.Y + ShadowOffset);
        var e = new Point(end.X + ShadowOffset, end.Y + ShadowOffset);
        return BuildPathForPoints(s, e, ShadowColor, leftEnd, rightEnd, lineStyle);
    }

    private static FrameworkElement BuildPathForPoints(Point start, Point end, Color color,
        ArrowheadStyle leftEnd, ArrowheadStyle rightEnd, ArrowLineStyle lineStyle)
    {
        double angle = ComputeAngle(start, end);
        var brush = new SolidColorBrush(color);

        // Shaft line (dashed/dotted applies here only)
        var shaftPath = new Path
        {
            Data = new LineGeometry(start, end),
            Stroke = brush,
            StrokeThickness = LineStrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        if (lineStyle == ArrowLineStyle.Dashed)
            shaftPath.StrokeDashArray = new DoubleCollection { 4, 3 };
        else if (lineStyle == ArrowLineStyle.Dotted)
            shaftPath.StrokeDashArray = new DoubleCollection { 1, 2 };

        // Arrowhead geometries (always solid stroke, no dash)
        var headGroup = new GeometryGroup();
        bool needsFill = false;

        var leftGeom = BuildArrowheadGeometry(start, angle + Math.PI, leftEnd);
        if (leftGeom != null && !leftGeom.IsEmpty())
        {
            headGroup.Children.Add(leftGeom);
            if (NeedsFill(leftEnd)) needsFill = true;
        }

        var rightGeom = BuildArrowheadGeometry(end, angle, rightEnd);
        if (rightGeom != null && !rightGeom.IsEmpty())
        {
            headGroup.Children.Add(rightGeom);
            if (NeedsFill(rightEnd)) needsFill = true;
        }

        // If no arrowheads, just return the shaft
        if (headGroup.Children.Count == 0)
            return shaftPath;

        // Combine shaft + arrowheads in a Canvas
        var canvas = new System.Windows.Controls.Canvas { IsHitTestVisible = false };
        shaftPath.IsHitTestVisible = false;
        canvas.Children.Add(shaftPath);

        var headPath = new Path
        {
            Data = headGroup,
            Stroke = brush,
            StrokeThickness = LineStrokeWidth,
            StrokeLineJoin = PenLineJoin.Bevel,
            IsHitTestVisible = false,
        };
        if (needsFill) headPath.Fill = brush;
        canvas.Children.Add(headPath);

        return canvas;
    }

    private static bool NeedsFill(ArrowheadStyle style) =>
        style is ArrowheadStyle.FilledTriangle or ArrowheadStyle.Barbed or ArrowheadStyle.DotEnd;

    public static PathGeometry? BuildArrowheadGeometry(Point tip, double angle, ArrowheadStyle style)
    {
        return style switch
        {
            ArrowheadStyle.FilledTriangle => BuildTriangleArrowhead(tip, angle),
            ArrowheadStyle.OpenArrowhead => BuildChevronArrowhead(tip, angle),
            ArrowheadStyle.Barbed => BuildBarbedArrowhead(tip, angle),
            ArrowheadStyle.DotEnd => BuildDotEndGeometry(tip),
            _ => null,
        };
    }

    private static PathGeometry BuildTriangleArrowhead(Point tip, double angle)
    {
        double back = angle + Math.PI;
        var left = new Point(tip.X + ArrowheadLength * Math.Cos(back + ArrowheadHalfAngleRad),
                             tip.Y + ArrowheadLength * Math.Sin(back + ArrowheadHalfAngleRad));
        var right = new Point(tip.X + ArrowheadLength * Math.Cos(back - ArrowheadHalfAngleRad),
                              tip.Y + ArrowheadLength * Math.Sin(back - ArrowheadHalfAngleRad));
        var fig = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(left, true));
        fig.Segments.Add(new LineSegment(right, true));
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    private static PathGeometry BuildChevronArrowhead(Point tip, double angle)
    {
        double back = angle + Math.PI;
        var left = new Point(tip.X + ArrowheadLength * Math.Cos(back + ArrowheadHalfAngleRad),
                             tip.Y + ArrowheadLength * Math.Sin(back + ArrowheadHalfAngleRad));
        var right = new Point(tip.X + ArrowheadLength * Math.Cos(back - ArrowheadHalfAngleRad),
                              tip.Y + ArrowheadLength * Math.Sin(back - ArrowheadHalfAngleRad));
        var arm1 = new PathFigure { StartPoint = left, IsClosed = false, IsFilled = false };
        arm1.Segments.Add(new LineSegment(tip, true));
        var arm2 = new PathFigure { StartPoint = tip, IsClosed = false, IsFilled = false };
        arm2.Segments.Add(new LineSegment(right, true));
        var g = new PathGeometry(); g.Figures.Add(arm1); g.Figures.Add(arm2); return g;
    }

    private static PathGeometry BuildBarbedArrowhead(Point tip, double angle)
    {
        double barbLength = ArrowheadLength;
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

    private static PathGeometry BuildDotEndGeometry(Point center)
    {
        const double radius = 5.0;
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
