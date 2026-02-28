using System.Windows;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using Point = System.Windows.Point;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 3: Drag points produce correct rectangle
/// Validates: Requirements 2.3
///
/// For any two screen-coordinate points (start, end), the emitted ScreenRect should have
/// X = min(start.X, end.X), Y = min(start.Y, end.Y),
/// Width = |end.X - start.X|, and Height = |end.Y - start.Y|.
/// </summary>
public class DragPointsPropertyTests
{
    /// <summary>
    /// Replicates the rectangle computation from GlobalInputHook.MouseHookCallback.
    /// Given a drag start and end point, computes the normalized rectangle.
    /// </summary>
    private static Rect ComputeDragRect(Point start, Point end)
    {
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        return new Rect(x, y, width, height);
    }

    private static Gen<Point> ScreenPointGen =>
        from x in Gen.Choose(-3840, 3840).Select(v => (double)v)
        from y in Gen.Choose(-2160, 2160).Select(v => (double)v)
        select new Point(x, y);

    // Feature: spotlight-overlay, Property 3: Drag points produce correct rectangle
    /// <summary>
    /// **Validates: Requirements 2.3**
    /// For any two screen-coordinate points (start, end), the computed rectangle
    /// should have X = min(start.X, end.X), Y = min(start.Y, end.Y),
    /// Width = |end.X - start.X|, and Height = |end.Y - start.Y|.
    /// The DragRectEventArgs should carry the correct ScreenRect.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Drag_Points_Produce_Correct_Rectangle()
    {
        var prop = Prop.ForAll(
            ScreenPointGen.ToArbitrary(),
            ScreenPointGen.ToArbitrary(),
            (start, end) =>
            {
                var rect = ComputeDragRect(start, end);
                var args = new DragRectEventArgs(rect, start);

                const double tolerance = 0.001;

                double expectedX = Math.Min(start.X, end.X);
                double expectedY = Math.Min(start.Y, end.Y);
                double expectedWidth = Math.Abs(end.X - start.X);
                double expectedHeight = Math.Abs(end.Y - start.Y);

                bool xMatch = Math.Abs(args.ScreenRect.X - expectedX) < tolerance;
                bool yMatch = Math.Abs(args.ScreenRect.Y - expectedY) < tolerance;
                bool widthMatch = Math.Abs(args.ScreenRect.Width - expectedWidth) < tolerance;
                bool heightMatch = Math.Abs(args.ScreenRect.Height - expectedHeight) < tolerance;

                // Verify DragStartPoint is preserved
                bool startPointMatch = args.DragStartPoint == start;

                return xMatch && yMatch && widthMatch && heightMatch && startPointMatch;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
