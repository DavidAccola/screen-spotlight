using System.Windows;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Input: saved nub state from settings.
/// </summary>
public record SavedNubState(
    double? NubFraction,
    AnchorEdge AnchorEdge,
    string MonitorFingerprint);

/// <summary>
/// Output: resolved position ready for FlyoutToolbarWindow to apply.
/// </summary>
public record ResolvedNubState(
    double NubScreenPos,   // DIP coordinate along edge axis (Y for L/R, X for Top)
    AnchorEdge AnchorEdge,
    Rect WorkArea);        // DIP work area of the target monitor

/// <summary>
/// Pure static validator that resolves a saved nub state against the current monitor list.
/// No WPF window dependencies — operates on plain Rect values and primitives.
/// </summary>
public static class NubPositionValidator
{
    public const AnchorEdge DefaultAnchorEdge = AnchorEdge.Left;

    /// <summary>
    /// Resolves a saved nub state against the current monitor list.
    /// Pure function — no side effects, no WPF calls.
    /// </summary>
    public static ResolvedNubState Resolve(
        SavedNubState saved,
        IReadOnlyList<MonitorInfo> monitors,
        double nubLength)
    {
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        // Step 1: Validate AnchorEdge — if not a defined enum value, substitute Right.
        AnchorEdge edge = Enum.IsDefined(typeof(AnchorEdge), saved.AnchorEdge)
            ? saved.AnchorEdge
            : AnchorEdge.Right;

        // Step 2: If NubFraction is null or MonitorFingerprint is empty → return DefaultCenter.
        if (saved.NubFraction is null || string.IsNullOrEmpty(saved.MonitorFingerprint))
            return DefaultCenter(primary, nubLength);

        // Step 3: Find monitor matching the fingerprint.
        MonitorInfo? target = monitors.FirstOrDefault(m =>
            MonitorHelper.BuildFingerprint(m.DeviceName, m.PhysicalWidth, m.PhysicalHeight)
                == saved.MonitorFingerprint);

        if (target is null)
            return DefaultCenter(primary, nubLength);

        // Step 4: Clamp NubFraction to [0.0, 1.0].
        double fraction = Math.Clamp(saved.NubFraction.Value, 0.0, 1.0);

        // Step 5: Determine edge length from target monitor's WorkArea.
        Rect workArea = target.WorkArea;
        double edgeLength = edge == AnchorEdge.Top ? workArea.Width : workArea.Height;

        // Step 6: If edgeLength <= 0 → fall back to Right edge, return DefaultCenter.
        if (edgeLength <= 0)
            return DefaultCenter(primary, nubLength);

        // Step 7: Compute dipCoord.
        // EdgeStart = WorkArea.Top for L/R, WorkArea.Left for Top.
        double edgeStart = edge == AnchorEdge.Top ? workArea.Left : workArea.Top;
        double dipCoord = edgeStart + fraction * (edgeLength - nubLength);

        // Step 8: Clamp dipCoord to [EdgeStart, EdgeStart + edgeLength - nubLength].
        double minCoord = edgeStart;
        double maxCoord = edgeStart + edgeLength - nubLength;
        // Ensure maxCoord >= minCoord even when nubLength > edgeLength (degenerate case).
        if (maxCoord < minCoord) maxCoord = minCoord;
        dipCoord = Math.Clamp(dipCoord, minCoord, maxCoord);

        // Step 9: Verify dipCoord is within any monitor's work area bounds.
        bool withinAnyMonitor = monitors.Any(m =>
        {
            Rect wa = m.WorkArea;
            if (edge == AnchorEdge.Top)
                return dipCoord >= wa.Left && dipCoord + nubLength <= wa.Right;
            else
                return dipCoord >= wa.Top && dipCoord + nubLength <= wa.Bottom;
        });

        if (!withinAnyMonitor)
            return DefaultCenter(primary, nubLength);

        // Step 10: Return resolved state.
        return new ResolvedNubState(dipCoord, edge, workArea);
    }

    /// <summary>
    /// Returns the default center position on the Right edge of the given monitor.
    /// </summary>
    public static ResolvedNubState DefaultCenter(MonitorInfo monitor, double nubLength)
    {
        Rect workArea = monitor.WorkArea;
        double nubScreenPos = workArea.Top + (workArea.Height - nubLength) / 2.0;
        return new ResolvedNubState(nubScreenPos, AnchorEdge.Right, workArea);
    }
}
