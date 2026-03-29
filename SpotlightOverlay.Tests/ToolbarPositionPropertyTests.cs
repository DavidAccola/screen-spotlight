using System.Windows;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: flyout-toolbar, Property 1: Edge-flush positioning
/// Validates: Requirements 2.2, 2.3, 2.4
///
/// For any valid AnchorEdge value and for any work area rectangle with positive
/// width and height, the toolbar position computed by ToolbarPositionCalculator.Calculate
/// shall place the toolbar flush against the specified edge.
/// </summary>
public class ToolbarPositionPropertyTests
{
    private const double Tolerance = 0.001;

    private static Gen<Rect> WorkAreaGen =>
        from x in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from y in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from w in Gen.Choose(100, 5000).Select(v => (double)v)
        from h in Gen.Choose(100, 5000).Select(v => (double)v)
        select new Rect(x, y, w, h);

    private static Gen<AnchorEdge> AnchorEdgeGen =>
        Gen.Elements(AnchorEdge.Left, AnchorEdge.Right, AnchorEdge.Top);

    /// <summary>
    /// Tuple of (toolbarWidth, toolbarHeight, handleThickness).
    /// </summary>
    private static Gen<(double Width, double Height, double Handle)> ToolbarDimensionsGen =>
        from w in Gen.Choose(50, 500).Select(v => (double)v)
        from h in Gen.Choose(100, 1000).Select(v => (double)v)
        from t in Gen.Choose(5, 30).Select(v => (double)v)
        select (w, h, t);

    // Feature: flyout-toolbar, Property 1: Edge-flush positioning
    /// <summary>
    /// **Validates: Requirements 2.2, 2.3, 2.4**
    /// For any work area, anchor edge, and toolbar dimensions, the toolbar
    /// position shall be flush against the specified edge:
    /// - Left anchor → position.Left == workArea.Left
    /// - Right anchor → position.Left + position.WindowWidth == workArea.Right
    /// - Top anchor → position.Top == workArea.Top
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_PlacesToolbar_FlushAgainstAnchorEdge()
    {
        var prop = Prop.ForAll(
            WorkAreaGen.ToArbitrary(),
            AnchorEdgeGen.ToArbitrary(),
            ToolbarDimensionsGen.ToArbitrary(),
            (workArea, edge, dims) =>
            {
                var position = ToolbarPositionCalculator.Calculate(
                    edge, workArea, dims.Width, dims.Height, dims.Handle);

                return edge switch
                {
                    AnchorEdge.Left =>
                        Math.Abs(position.Left - workArea.Left) < Tolerance,
                    AnchorEdge.Right =>
                        Math.Abs((position.Left + position.WindowWidth) - workArea.Right) < Tolerance,
                    AnchorEdge.Top =>
                        Math.Abs(position.Top - workArea.Top) < Tolerance,
                    _ => false
                };
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 2: Perpendicular-axis centering
    /// <summary>
    /// **Validates: Requirements 2.5, 2.6**
    /// For any work area, anchor edge, and toolbar dimensions that fit within
    /// the work area, the toolbar position shall be centered on the perpendicular axis:
    /// - Left/Right anchor → position.Top == workArea.Top + (workArea.Height - toolbarHeight) / 2
    /// - Top anchor → position.Left == workArea.Left + (workArea.Width - toolbarWidth) / 2
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_CentersToolbar_OnPerpendicularAxis()
    {
        var prop = Prop.ForAll(
            WorkAreaGen.ToArbitrary(),
            AnchorEdgeGen.ToArbitrary(),
            ToolbarDimensionsGen.ToArbitrary(),
            (workArea, edge, dims) =>
            {
                var position = ToolbarPositionCalculator.Calculate(
                    edge, workArea, dims.Width, dims.Height, dims.Handle);

                return edge switch
                {
                    AnchorEdge.Left or AnchorEdge.Right =>
                        Math.Abs(position.Top - (workArea.Top + (workArea.Height - dims.Height) / 2)) < Tolerance,
                    AnchorEdge.Top =>
                        Math.Abs(position.Left - (workArea.Left + (workArea.Width - dims.Width) / 2)) < Tolerance,
                    _ => false
                };
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 5: Position stays within work area bounds
    /// <summary>
    /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 2.6, 8.2**
    /// For any work area, anchor edge, and toolbar dimensions that are smaller
    /// than the work area, the computed window rectangle shall be fully contained
    /// within the work area bounds.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_PositionStaysWithinWorkAreaBounds()
    {
        var gen = from workArea in WorkAreaGen
                  from edge in AnchorEdgeGen
                  from w in Gen.Choose(10, Math.Max(10, (int)Math.Min(workArea.Width, 500)))
                  from h in Gen.Choose(10, Math.Max(10, (int)Math.Min(workArea.Height, 1000)))
                  from t in Gen.Choose(5, 30)
                  select (workArea, edge, (double)w, (double)h, (double)t);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (workArea, edge, toolbarWidth, toolbarHeight, handleThickness) = input;

                var position = ToolbarPositionCalculator.Calculate(
                    edge, workArea, toolbarWidth, toolbarHeight, handleThickness);

                var leftOk = position.Left >= workArea.Left - Tolerance;
                var topOk = position.Top >= workArea.Top - Tolerance;
                var rightOk = position.Left + position.WindowWidth <= workArea.Right + Tolerance;
                var bottomOk = position.Top + position.WindowHeight <= workArea.Bottom + Tolerance;

                return leftOk && topOk && rightOk && bottomOk;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
