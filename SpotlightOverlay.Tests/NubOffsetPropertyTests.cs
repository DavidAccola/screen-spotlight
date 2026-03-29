using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: flyout-toolbar
/// Property tests for NubOffsetCalculator.
/// </summary>
public class NubOffsetPropertyTests
{
    private static Gen<AnchorEdge> AnchorEdgeGen =>
        Gen.Elements(AnchorEdge.Left, AnchorEdge.Right, AnchorEdge.Top);

    // Feature: flyout-toolbar, Property 7: Default nub offset is zero when not at boundary
    /// <summary>
    /// **Validates: Requirements 12.1, 12.2**
    /// For any valid AnchorEdge, any work area, any toolbar face length greater than
    /// nubLength, and any cursor position along the edge such that the toolbar does not
    /// reach a work area boundary, NubOffsetCalculator.Calculate shall return a nubOffset of 0.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_NubOffsetIsZero_WhenToolbarNotAtBoundary()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(100, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, (int)nubLength + 500).Select(v => (double)v)
            from edge in AnchorEdgeGen
            // Cursor must be such that toolbar centered on cursor fits entirely within work area:
            // cursorAlongEdge - faceLength/2 >= workAreaStart  AND  cursorAlongEdge + faceLength/2 <= workAreaEnd
            // i.e. cursor in [workAreaStart + faceLength/2, workAreaEnd - faceLength/2]
            let minCursor = (int)Math.Ceiling(workAreaStart + faceLength / 2)
            let maxCursor = (int)Math.Floor(workAreaEnd - faceLength / 2)
            where maxCursor >= minCursor
            from cursor in Gen.Choose(minCursor, maxCursor).Select(v => (double)v)
            select (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (_, nubOffset) = NubOffsetCalculator.Calculate(
                    edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                return nubOffset == 0;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 8: Nub offset clamped range invariant
    /// <summary>
    /// **Validates: Requirements 13.3, 15.3, 15.4**
    /// For any valid AnchorEdge, any work area, any toolbar face length greater than
    /// nubLength, and any cursor position along the edge, the nubOffset returned by
    /// NubOffsetCalculator.Calculate shall be in the range [0, toolbarFaceLength - nubLength].
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_NubOffset_IsWithinClampedRange()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(200, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, Math.Max((int)nubLength + 1, (int)workAreaLength)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            from cursor in Gen.Choose((int)workAreaStart - 500, (int)workAreaEnd + 500).Select(v => (double)v)
            select (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (_, nubOffset) = NubOffsetCalculator.Calculate(
                    edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                return nubOffset >= 0 && nubOffset <= faceLength - nubLength;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 9: Toolbar position clamped to work area
    /// <summary>
    /// **Validates: Requirements 13.1**
    /// For any valid AnchorEdge, any work area, any toolbar face length that fits within
    /// the work area, and any cursor position, the toolbarPos returned by
    /// NubOffsetCalculator.Calculate shall satisfy toolbarPos >= workAreaStart and
    /// toolbarPos + toolbarFaceLength <= workAreaEnd.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_ToolbarPosition_IsClampedToWorkArea()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(100, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from faceLength in Gen.Choose(10, (int)workAreaLength).Select(v => (double)v)
            from nubLength in Gen.Choose(5, Math.Max(5, (int)faceLength - 1)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            from cursor in Gen.Choose((int)workAreaStart - 1000, (int)workAreaEnd + 1000).Select(v => (double)v)
            select (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (toolbarPos, _) = NubOffsetCalculator.Calculate(
                    edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                return toolbarPos >= workAreaStart && toolbarPos + faceLength <= workAreaEnd;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 10: Nub offset increases at boundary
    /// <summary>
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// For any valid AnchorEdge, any work area where the toolbar is in boundary contact,
    /// and two cursor positions c1 and c2 both past the boundary where c2 is further from
    /// the default end than c1, the nubOffset for c2 shall be >= the nubOffset for c1.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_NubOffset_IncreasesAtBoundary()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(200, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, Math.Max((int)nubLength + 1, (int)workAreaLength)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            from nearStart in Gen.Elements(true, false)
            // Boundary contact occurs when cursor causes toolbar to clamp.
            // Near start: cursor < workAreaStart + faceLength/2 → toolbar clamps to workAreaStart
            // Near end:   cursor > workAreaEnd - faceLength/2  → toolbar clamps to workAreaEnd - faceLength
            // The nub offset formula is: clamp(cursor - clampedPos - nubLength/2, 0, faceLength - nubLength)
            // Since clampedPos is constant for both cursors (same boundary), offset is monotonically
            // non-decreasing in cursor. So c2 > c1 → nubOffset(c2) >= nubOffset(c1) for both sides.
            let boundaryThreshold = nearStart
                ? (int)Math.Floor(workAreaStart + faceLength / 2) - 1
                : (int)Math.Ceiling(workAreaEnd - faceLength / 2) + 1
            let rangeMin = nearStart
                ? (int)Math.Ceiling(workAreaStart)
                : boundaryThreshold
            let rangeMax = nearStart
                ? boundaryThreshold
                : (int)Math.Floor(workAreaEnd)
            where rangeMax > rangeMin + 1
            from cursorA in Gen.Choose(rangeMin, rangeMax).Select(v => (double)v)
            from cursorB in Gen.Choose(rangeMin, rangeMax).Select(v => (double)v)
            where cursorA != cursorB
            let c1 = Math.Min(cursorA, cursorB)
            let c2 = Math.Max(cursorA, cursorB)
            select (edge, c1, c2, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, c1, c2, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (_, nubOffset1) = NubOffsetCalculator.Calculate(
                    edge, c1, workAreaStart, workAreaEnd, faceLength, nubLength);

                var (_, nubOffset2) = NubOffsetCalculator.Calculate(
                    edge, c2, workAreaStart, workAreaEnd, faceLength, nubLength);

                return nubOffset2 >= nubOffset1;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 11: Boundary departure resets nub offset
    /// <summary>
    /// **Validates: Requirements 12.3**
    /// For any valid AnchorEdge, any work area, any toolbar face length greater than
    /// nubLength that fits in the work area, and any cursor position that does NOT cause
    /// boundary contact, NubOffsetCalculator.Calculate shall return a nubOffset of 0.
    /// This verifies that when the cursor moves from a boundary-contact position to a
    /// non-boundary position, the nub offset resets to the default.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_NubOffset_ResetsWhenLeavingBoundary()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(200, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, Math.Max((int)nubLength + 1, (int)workAreaLength)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            // Step 2: Generate a cursor that causes boundary contact
            // Near start: cursor < workAreaStart + faceLength/2
            // Near end:   cursor > workAreaEnd - faceLength/2
            from nearStart in Gen.Elements(true, false)
            let boundaryMin = nearStart
                ? (int)Math.Ceiling(workAreaStart)
                : (int)Math.Ceiling(workAreaEnd - faceLength / 2) + 1
            let boundaryMax = nearStart
                ? (int)Math.Floor(workAreaStart + faceLength / 2) - 1
                : (int)Math.Floor(workAreaEnd)
            where boundaryMax >= boundaryMin
            from boundaryCursor in Gen.Choose(boundaryMin, boundaryMax).Select(v => (double)v)
            // Step 3: Generate a cursor that does NOT cause boundary contact
            let safeMin = (int)Math.Ceiling(workAreaStart + faceLength / 2)
            let safeMax = (int)Math.Floor(workAreaEnd - faceLength / 2)
            where safeMax >= safeMin
            from safeCursor in Gen.Choose(safeMin, safeMax).Select(v => (double)v)
            select (edge, boundaryCursor, safeCursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, boundaryCursor, safeCursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                // First call at boundary — may produce non-zero nub offset
                var (_, boundaryNubOffset) = NubOffsetCalculator.Calculate(
                    edge, boundaryCursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                // Second call at non-boundary position — nub offset must reset to 0
                var (_, safeNubOffset) = NubOffsetCalculator.Calculate(
                    edge, safeCursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                return safeNubOffset == 0;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 12: Nub cursor tracking at boundary
    /// <summary>
    /// **Validates: Requirements 13.2, 13.4, 13.5**
    /// For any valid AnchorEdge, any work area, and any cursor position that causes
    /// boundary contact, the nub's absolute center position along the edge
    /// (toolbarPos + nubOffset + nubLength/2) shall be as close to the cursor position
    /// as the clamped range allows.
    /// The valid range for the nub center is:
    ///   [toolbarPos + nubLength/2, toolbarPos + faceLength - nubLength/2]
    /// So expectedCenter = Clamp(cursor, toolbarPos + nubLength/2, toolbarPos + faceLength - nubLength/2)
    /// and |nubCenter - expectedCenter| < 0.001.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_NubCenter_TracksCursorAtBoundary()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(200, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, Math.Max((int)nubLength + 1, (int)workAreaLength)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            // Cursor must cause boundary contact:
            // Near start: cursor < workAreaStart + faceLength/2
            // Near end:   cursor > workAreaEnd - faceLength/2
            from nearStart in Gen.Elements(true, false)
            let boundaryMin = nearStart
                ? (int)Math.Ceiling(workAreaStart)
                : (int)Math.Ceiling(workAreaEnd - faceLength / 2) + 1
            let boundaryMax = nearStart
                ? (int)Math.Floor(workAreaStart + faceLength / 2) - 1
                : (int)Math.Floor(workAreaEnd)
            where boundaryMax >= boundaryMin
            from cursor in Gen.Choose(boundaryMin, boundaryMax).Select(v => (double)v)
            select (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (toolbarPos, nubOffset) = NubOffsetCalculator.Calculate(
                    edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                double nubCenter = toolbarPos + nubOffset + nubLength / 2;

                double minCenter = toolbarPos + nubLength / 2;
                double maxCenter = toolbarPos + faceLength - nubLength / 2;
                double expectedCenter = Math.Clamp(cursor, minCenter, maxCenter);

                return Math.Abs(nubCenter - expectedCenter) < 0.001;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: nub-expand-position-fix, Property 2a: Nub screen position composition
    /// <summary>
    /// **Validates: Requirements 3.2**
    /// Preservation test: for all (anchorEdge, cursorAlongEdge, workArea, faceLength, nubLength)
    /// where faceLength > nubLength and workArea is non-degenerate, NubOffsetCalculator.Calculate
    /// returns (toolbarPos, nubOffset) where:
    ///   1. nubOffset is in [0, faceLength - nubLength]
    ///   2. The nub screen position (toolbarPos + nubOffset) is within the toolbar face bounds:
    ///      toolbarPos + nubOffset >= toolbarPos AND toolbarPos + nubOffset + nubLength <= toolbarPos + faceLength
    ///   3. The nub screen position is within the work area:
    ///      toolbarPos + nubOffset >= workAreaStart AND toolbarPos + nubOffset + nubLength <= workAreaEnd
    /// This verifies the composite relationship that SnapToEdgeAtCursor relies on:
    /// _nubScreenPos = toolbarPos + nubOffset produces a valid screen position.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_NubScreenPosition_IsToolbarPlusOffset_AndWithinBounds()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(200, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, Math.Max((int)nubLength + 1, (int)workAreaLength)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            from cursor in Gen.Choose((int)workAreaStart, (int)workAreaEnd).Select(v => (double)v)
            select (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (toolbarPos, nubOffset) = NubOffsetCalculator.Calculate(
                    edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                // nubOffset must be in valid range
                bool offsetInRange = nubOffset >= 0 && nubOffset <= faceLength - nubLength;

                // The nub's screen position is toolbarPos + nubOffset
                double nubScreenPos = toolbarPos + nubOffset;

                // Nub must fit within the toolbar face
                bool nubWithinToolbar = nubScreenPos >= toolbarPos
                    && nubScreenPos + nubLength <= toolbarPos + faceLength;

                // Nub must be within the work area
                bool nubWithinWorkArea = nubScreenPos >= workAreaStart
                    && nubScreenPos + nubLength <= workAreaEnd;

                return offsetInRange && nubWithinToolbar && nubWithinWorkArea;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: nub-expand-position-fix, Property 2b: Toolbar stays within work area
    /// <summary>
    /// **Validates: Requirements 3.2, 3.6**
    /// Preservation test: for all non-degenerate inputs where faceLength > nubLength
    /// and faceLength fits within the work area, the toolbar position returned by
    /// NubOffsetCalculator.Calculate satisfies toolbarPos >= workAreaStart and
    /// toolbarPos + faceLength <= workAreaEnd (toolbar stays entirely within work area).
    /// </summary>
    [Property(MaxTest = 100)]
    public void Calculate_ToolbarStaysInWorkArea_ForNonDegenerateInputs()
    {
        var gen =
            from workAreaStart in Gen.Choose(-5000, 5000).Select(v => (double)v)
            from workAreaLength in Gen.Choose(200, 5000).Select(v => (double)v)
            let workAreaEnd = workAreaStart + workAreaLength
            from nubLength in Gen.Choose(10, 100).Select(v => (double)v)
            from faceLength in Gen.Choose((int)nubLength + 1, Math.Max((int)nubLength + 1, (int)workAreaLength)).Select(v => (double)v)
            from edge in AnchorEdgeGen
            from cursor in Gen.Choose((int)workAreaStart - 500, (int)workAreaEnd + 500).Select(v => (double)v)
            select (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength) = input;

                var (toolbarPos, _) = NubOffsetCalculator.Calculate(
                    edge, cursor, workAreaStart, workAreaEnd, faceLength, nubLength);

                return toolbarPos >= workAreaStart && toolbarPos + faceLength <= workAreaEnd;
            });

        prop.QuickCheckThrowOnFailure();
    }

}
