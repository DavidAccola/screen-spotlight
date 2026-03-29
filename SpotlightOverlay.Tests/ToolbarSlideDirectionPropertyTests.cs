using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: flyout-toolbar, Property 4: Slide direction consistency
/// Validates: Requirements 4.2, 4.3, 4.4
///
/// For any valid AnchorEdge value, the slide direction returned by
/// SlideDirectionHelper.GetSlideDirection shall always move away from the
/// anchor edge on expand (Left→right, Right→left, Top→down), and the axis
/// (horizontal vs vertical) shall match the anchor orientation.
/// </summary>
public class ToolbarSlideDirectionPropertyTests
{
    private static Gen<AnchorEdge> AnchorEdgeGen =>
        Gen.Elements(AnchorEdge.Left, AnchorEdge.Right, AnchorEdge.Top);

    // Feature: flyout-toolbar, Property 4: Slide direction consistency
    /// <summary>
    /// **Validates: Requirements 4.2, 4.3, 4.4**
    /// For all valid AnchorEdge values, GetSlideDirection returns the correct
    /// (isHorizontal, expandSign) mapping:
    ///   Left  → (true, +1.0)
    ///   Right → (true, -1.0)
    ///   Top   → (false, +1.0)
    /// </summary>
    [Property(MaxTest = 100)]
    public void SlideDirection_Returns_Correct_Mapping_For_All_AnchorEdges()
    {
        var prop = Prop.ForAll(
            AnchorEdgeGen.ToArbitrary(),
            (AnchorEdge edge) =>
            {
                var (isHorizontal, expandSign) = SlideDirectionHelper.GetSlideDirection(edge);

                var (expectedHorizontal, expectedSign) = edge switch
                {
                    AnchorEdge.Left  => (true, +1.0),
                    AnchorEdge.Right => (true, -1.0),
                    AnchorEdge.Top   => (false, +1.0),
                    _ => throw new ArgumentOutOfRangeException(nameof(edge))
                };

                return isHorizontal == expectedHorizontal && expandSign == expectedSign;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 4: Slide direction consistency
    /// <summary>
    /// **Validates: Requirements 4.2, 4.3, 4.4**
    /// Left and Right anchors use horizontal axis (isHorizontal=true),
    /// Top anchor uses vertical axis (isHorizontal=false).
    /// </summary>
    [Property(MaxTest = 100)]
    public void SlideDirection_Axis_Matches_Anchor_Orientation()
    {
        var prop = Prop.ForAll(
            AnchorEdgeGen.ToArbitrary(),
            (AnchorEdge edge) =>
            {
                var (isHorizontal, _) = SlideDirectionHelper.GetSlideDirection(edge);

                return edge switch
                {
                    AnchorEdge.Left or AnchorEdge.Right => isHorizontal == true,
                    AnchorEdge.Top => isHorizontal == false,
                    _ => false
                };
            });

        prop.QuickCheckThrowOnFailure();
    }
}
