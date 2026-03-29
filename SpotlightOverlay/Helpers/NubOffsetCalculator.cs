using SpotlightOverlay.Models;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Pure static calculator that computes the clamped toolbar position and sliding nub
/// offset for a given drag cursor position along the anchor edge axis.
/// </summary>
public static class NubOffsetCalculator
{
    /// <summary>
    /// Computes the nub offset and clamped toolbar position given a drag cursor position.
    /// </summary>
    /// <param name="edge">Current anchor edge (accepted for API consistency; algorithm is axis-independent).</param>
    /// <param name="cursorAlongEdge">Cursor position along the anchor edge axis (Y for L/R, X for Top).</param>
    /// <param name="workAreaStart">Work area start along the anchor edge axis.</param>
    /// <param name="workAreaEnd">Work area end along the anchor edge axis.</param>
    /// <param name="toolbarFaceLength">Length of the toolbar face (height for L/R, width for Top).</param>
    /// <param name="nubLength">Length of the nub along the face.</param>
    /// <returns>
    /// A tuple where <c>toolbarPos</c> is the clamped toolbar position along the edge,
    /// and <c>nubOffset</c> is the nub's offset along the toolbar face [0, faceLength - nubLength].
    /// </returns>
    public static (double toolbarPos, double nubOffset) Calculate(
        AnchorEdge edge,
        double cursorAlongEdge,
        double workAreaStart,
        double workAreaEnd,
        double toolbarFaceLength,
        double nubLength)
    {
        // Edge case: degenerate work area
        if (workAreaEnd <= workAreaStart)
            return (workAreaStart, 0);

        // Edge case: nub is as large as or larger than the toolbar face
        if (toolbarFaceLength <= nubLength)
        {
            double degeneratePos = Math.Clamp(
                cursorAlongEdge - toolbarFaceLength / 2,
                workAreaStart,
                workAreaEnd - toolbarFaceLength);
            return (degeneratePos, 0);
        }

        // Step 1: desired toolbar position centers toolbar on cursor
        double desiredToolbarPos = cursorAlongEdge - toolbarFaceLength / 2;

        // Step 2: clamp toolbar position to work area
        double clampedToolbarPos = Math.Clamp(
            desiredToolbarPos,
            workAreaStart,
            workAreaEnd - toolbarFaceLength);

        // Step 5 (early): if toolbar is not at a boundary, nub stays at default
        if (clampedToolbarPos == desiredToolbarPos)
            return (clampedToolbarPos, 0);

        // Step 3: nub's desired position relative to toolbar
        double nubRelative = cursorAlongEdge - clampedToolbarPos - nubLength / 2;

        // Step 4: clamp nub offset within valid range
        double nubOffset = Math.Clamp(nubRelative, 0, toolbarFaceLength - nubLength);

        return (clampedToolbarPos, nubOffset);
    }
}
