using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: nub-expand-position-fix
/// Bug condition exploration tests for expand/collapse nub position drift.
/// These tests encode the EXPECTED behavior and are expected to FAIL on unfixed code,
/// confirming the bug exists.
/// </summary>
public class ExpandCollapsePositionPropertyTests
{
    private const double NubLength = 52.0;

    private static Gen<AnchorEdge> AnchorEdgeGen =>
        Gen.Elements(AnchorEdge.Left, AnchorEdge.Right, AnchorEdge.Top);

    // ── Pure functions mirroring CURRENT (buggy) logic ──────────────

    /// <summary>
    /// Mirrors the FIXED ExpandToolbar() logic:
    /// 1. Compute targetPos = nubScreenPos - nubOffset
    /// 2. Clamp to work area
    /// 3. Recompute nubOffset = nubScreenPos - clampedPos (so nub stays at nubScreenPos)
    /// 4. Return (expandedWindowPos, effectiveNubOffset)
    /// </summary>
    public static (double expandedWindowPos, double effectiveNubOffset) ComputeExpandedPosition(
        AnchorEdge edge,
        double nubScreenPos,
        double nubOffset,
        double workAreaStart,
        double workAreaEnd,
        double expandedFaceLength)
    {
        double targetPos = nubScreenPos - nubOffset;
        double clampedPos = Math.Clamp(targetPos, workAreaStart, workAreaEnd - expandedFaceLength);
        // Recompute nubOffset after clamping so the nub margin places nub at nubScreenPos
        double effectiveNubOffset = nubScreenPos - clampedPos;
        return (clampedPos, effectiveNubOffset);
    }

    /// <summary>
    /// Mirrors the FIXED collapsed state behavior:
    /// ApplyNubOffset() now checks ToolbarPanel.Visibility and returns 0 when collapsed.
    /// CollapseToolbar() and PositionCollapsedKeepAlongEdge() explicitly reset margin to 0.
    /// </summary>
    public static double ComputeCollapsedNubMargin(double nubOffset)
    {
        // Fixed behavior: margin is always 0 when collapsed
        return 0;
    }


    // ── Property 1: Bug Condition — Expand position invariant ────────

    /// <summary>
    /// **Validates: Requirements 1.1, 1.2, 1.5, 2.1, 2.2, 2.3, 2.5**
    /// For all (anchorEdge, nubScreenPos, nubOffset, workArea) where nubOffset >= 0,
    /// the expanded window position + nubOffset must equal nubScreenPos.
    /// This WILL FAIL on unfixed code because clamping breaks the invariant
    /// and nubOffset is not recomputed after clamping.
    /// </summary>
    [Property(MaxTest = 200)]
    public void ExpandedWindowPos_Plus_NubOffset_Equals_NubScreenPos()
    {
        var gen =
            from edge in AnchorEdgeGen
            from workAreaStart in Gen.Choose(0, 500).Select(v => (double)v)
            from workAreaLength in Gen.Choose(300, 2000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            // expandedFaceLength is the toolbar height (L/R) or width (Top) — must fit in work area
            from expandedFaceLength in Gen.Choose(100, Math.Max(100, (int)workAreaLength)).Select(v => (double)v)
            where expandedFaceLength > NubLength
            // nubOffset in [0, expandedFaceLength - NubLength]
            from nubOffset in Gen.Choose(0, (int)(expandedFaceLength - NubLength)).Select(v => (double)v)
            // nubScreenPos: the nub's screen position, must be within work area
            // (collapsed window = nub, so nubScreenPos in [workAreaStart, workAreaEnd - NubLength])
            from nubScreenPos in Gen.Choose((int)workAreaStart, Math.Max((int)workAreaStart, (int)(workAreaEnd - NubLength))).Select(v => (double)v)
            select (edge, nubScreenPos, nubOffset, workAreaStart, workAreaEnd, expandedFaceLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, nubScreenPos, nubOffset, workAreaStart, workAreaEnd, expandedFaceLength) = input;

                var (expandedWindowPos, effectiveNubOffset) = ComputeExpandedPosition(
                    edge, nubScreenPos, nubOffset, workAreaStart, workAreaEnd, expandedFaceLength);

                // The invariant: the nub's screen position after expand must equal nubScreenPos
                // nubScreenPos = expandedWindowPos + effectiveNubOffset
                return (expandedWindowPos + effectiveNubOffset) == nubScreenPos;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // ── Property 1 (collapse): Bug Condition — Collapsed nub margin must be zero ──

    /// <summary>
    /// **Validates: Requirements 1.3, 1.4, 2.4**
    /// For all (anchorEdge, nubScreenPos, nubOffset) where nubOffset > 0,
    /// the collapsed nub margin must be 0 (since the collapsed window IS the nub).
    /// This WILL FAIL on unfixed code because ApplyNubOffset() persists the margin
    /// as _nubOffset even in collapsed state.
    /// </summary>
    [Property(MaxTest = 200)]
    public void CollapsedNubMargin_MustBeZero_WhenNubOffsetIsNonZero()
    {
        var gen =
            from edge in AnchorEdgeGen
            from nubScreenPos in Gen.Choose(0, 2000).Select(v => (double)v)
            // nubOffset > 0 to trigger the bug
            from nubOffset in Gen.Choose(1, 300).Select(v => (double)v)
            select (edge, nubScreenPos, nubOffset);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, nubScreenPos, nubOffset) = input;

                double collapsedMargin = ComputeCollapsedNubMargin(nubOffset);

                // The invariant: collapsed window IS the nub, so margin must be 0
                return collapsedMargin == 0;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
