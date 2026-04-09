using System.Windows;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: toolbar-nub-persistence
/// Property tests for NubPositionValidator.Resolve.
/// </summary>
public class NubPositionValidatorPropertyTests
{
    // ── Generators ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a Rect with positive width and height, positioned anywhere in the virtual desktop.
    /// </summary>
    private static Gen<Rect> WorkAreaGen =>
        from x in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from y in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from w in Gen.Choose(100, 3000).Select(v => (double)v)
        from h in Gen.Choose(100, 3000).Select(v => (double)v)
        select new Rect(x, y, w, h);

    /// <summary>
    /// Generates a MonitorInfo with a positive work area.
    /// </summary>
    private static Gen<MonitorInfo> MonitorInfoGen(bool isPrimary) =>
        from workArea in WorkAreaGen
        from deviceName in Arb.Generate<NonEmptyString>().Select(s => s.Get)
        from physW in Gen.Choose(640, 7680)
        from physH in Gen.Choose(480, 4320)
        select new MonitorInfo(deviceName, physW, physH, workArea, isPrimary);

    /// <summary>
    /// Generates a non-empty list of monitors with exactly one primary.
    /// </summary>
    private static Gen<IReadOnlyList<MonitorInfo>> MonitorListGen =>
        from primary in MonitorInfoGen(isPrimary: true)
        from extraCount in Gen.Choose(0, 3)
        from extras in Gen.ListOf(extraCount, MonitorInfoGen(isPrimary: false))
        select (IReadOnlyList<MonitorInfo>)[primary, .. extras];

    /// <summary>
    /// Generates a NubFraction in the extended range [-1.0, 2.0] (includes out-of-range values).
    /// </summary>
    private static Gen<double?> NubFractionGen =>
        Gen.Choose(-100, 200).Select(v => (double?)((double)v / 100.0));

    /// <summary>
    /// Generates an AnchorEdge int that may be invalid (outside defined enum values).
    /// </summary>
    private static Gen<AnchorEdge> AnyAnchorEdgeGen =>
        Gen.Choose(-2, 5).Select(v => (AnchorEdge)v);

    /// <summary>
    /// Generates a nub length in a realistic range.
    /// </summary>
    private static Gen<double> NubLengthGen =>
        Gen.Choose(10, 80).Select(v => (double)v);

    // ── Property 2: Resolved DIP coordinate is always within work area bounds ──

    // Feature: toolbar-nub-persistence, Property 2: Resolved DIP within bounds
    /// <summary>
    /// **Validates: Requirements 2.3, 4.3, 6.2**
    /// For any SavedNubState (fraction ∈ [-1.0, 2.0], any AnchorEdge int, any fingerprint)
    /// and any MonitorInfo list with at least one primary monitor and positive work areas,
    /// NubPositionValidator.Resolve must return a NubScreenPos that lies within the target
    /// monitor's work area bounds along the resolved edge axis.
    /// </summary>
    [Property(MaxTest = 200)]
    public void Resolve_NubScreenPos_IsWithinWorkAreaBounds()
    {
        var gen =
            from monitors in MonitorListGen
            from fraction in NubFractionGen
            from edge in AnyAnchorEdgeGen
            from fingerprint in Arb.Generate<string>().Select(s => s ?? "")
            from nubLength in NubLengthGen
            select (monitors, fraction, edge, fingerprint, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (monitors, fraction, edge, fingerprint, nubLength) = input;

                var saved = new SavedNubState(fraction, edge, fingerprint);
                var resolved = NubPositionValidator.Resolve(saved, monitors, nubLength);

                Rect wa = resolved.WorkArea;
                double pos = resolved.NubScreenPos;

                // For Left/Right edges: pos is a Y coordinate, must be within [wa.Top, wa.Bottom - nubLength]
                // For Top edge: pos is an X coordinate, must be within [wa.Left, wa.Right - nubLength]
                bool inBounds = resolved.AnchorEdge == AnchorEdge.Top
                    ? pos >= wa.Left - 0.001 && pos + nubLength <= wa.Right + 0.001
                    : pos >= wa.Top - 0.001 && pos + nubLength <= wa.Bottom + 0.001;

                return inBounds;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // ── Property 3: Missing monitor always falls back to primary center ──────

    // Feature: toolbar-nub-persistence, Property 3: Missing monitor fallback
    /// <summary>
    /// **Validates: Requirements 3.1, 5.2**
    /// For any SavedNubState with fingerprint not in monitor list,
    /// NubPositionValidator.Resolve must return the primary monitor's work area
    /// and a NubScreenPos equal to the center of the primary monitor's Right edge.
    /// </summary>
    [Property(MaxTest = 200)]
    public void Resolve_MissingMonitor_FallsBackToPrimaryCenter()
    {
        var gen =
            from monitors in MonitorListGen
            from fraction in NubFractionGen
            from edge in AnyAnchorEdgeGen
            from nubLength in NubLengthGen
            // Build a fingerprint that is guaranteed NOT to match any monitor in the list.
            let usedFingerprints = monitors
                .Select(m => MonitorHelper.BuildFingerprint(m.DeviceName, m.PhysicalWidth, m.PhysicalHeight))
                .ToHashSet()
            let absentFingerprint = "ABSENT|9999x9999_unique_" + Guid.NewGuid().ToString("N")
            where !usedFingerprints.Contains(absentFingerprint)
            select (monitors, fraction, edge, absentFingerprint, nubLength);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (monitors, fraction, edge, fingerprint, nubLength) = input;

                var saved = new SavedNubState(fraction, edge, fingerprint);
                var resolved = NubPositionValidator.Resolve(saved, monitors, nubLength);

                var primary = monitors.First(m => m.IsPrimary);
                Rect primaryWa = primary.WorkArea;

                double expectedPos = primaryWa.Top + (primaryWa.Height - nubLength) / 2.0;

                bool workAreaMatches = resolved.WorkArea == primaryWa;
                bool posMatches = Math.Abs(resolved.NubScreenPos - expectedPos) < 0.001;
                bool edgeIsRight = resolved.AnchorEdge == AnchorEdge.Right;

                return workAreaMatches && posMatches && edgeIsRight;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // ── Property 5: Monitor matched by device name regardless of virtual desktop position ──

    // Feature: toolbar-nub-persistence, Property 5: Monitor matched by device name
    /// <summary>
    /// **Validates: Requirements 5.1**
    /// For any monitor list where one monitor's fingerprint matches the saved fingerprint,
    /// NubPositionValidator.Resolve must use that monitor's work area — regardless of what
    /// Left/Top values that monitor has in the virtual desktop.
    /// </summary>
    [Property(MaxTest = 200)]
    public void Resolve_MatchedMonitor_UsesMatchedMonitorWorkArea()
    {
        var gen =
            from nubLength in NubLengthGen
            // Generate the target monitor with a known device name and physical size.
            from deviceName in Arb.Generate<NonEmptyString>().Select(s => s.Get)
            from physW in Gen.Choose(640, 3840)
            from physH in Gen.Choose(480, 2160)
            // Generate two different work areas for the same monitor (simulating virtual desktop shift).
            from workArea1 in WorkAreaGen
            from workArea2 in WorkAreaGen
            // Build the fingerprint for the target monitor.
            let fingerprint = MonitorHelper.BuildFingerprint(deviceName, physW, physH)
            // Build a valid fraction so we don't hit the null/empty fallback.
            from fraction in Gen.Choose(0, 100).Select(v => (double?)((double)v / 100.0))
            from edge in Gen.Elements(AnchorEdge.Left, AnchorEdge.Right, AnchorEdge.Top)
            // Build a primary monitor that does NOT share the fingerprint.
            from primaryDeviceName in Arb.Generate<NonEmptyString>().Select(s => s.Get)
            from primaryPhysW in Gen.Choose(640, 3840)
            from primaryPhysH in Gen.Choose(480, 2160)
            from primaryWorkArea in WorkAreaGen
            where MonitorHelper.BuildFingerprint(primaryDeviceName, primaryPhysW, primaryPhysH) != fingerprint
            select (nubLength, deviceName, physW, physH, workArea1, workArea2, fingerprint,
                    fraction, edge, primaryDeviceName, primaryPhysW, primaryPhysH, primaryWorkArea);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (nubLength, deviceName, physW, physH, workArea1, workArea2, fingerprint,
                     fraction, edge, primaryDeviceName, primaryPhysW, primaryPhysH, primaryWorkArea) = input;

                // Build two monitor lists: same fingerprint but different virtual desktop positions.
                var targetMonitor1 = new MonitorInfo(deviceName, physW, physH, workArea1, IsPrimary: false);
                var targetMonitor2 = new MonitorInfo(deviceName, physW, physH, workArea2, IsPrimary: false);
                var primary = new MonitorInfo(primaryDeviceName, primaryPhysW, primaryPhysH, primaryWorkArea, IsPrimary: true);

                var monitors1 = (IReadOnlyList<MonitorInfo>)[primary, targetMonitor1];
                var monitors2 = (IReadOnlyList<MonitorInfo>)[primary, targetMonitor2];

                var saved = new SavedNubState(fraction, edge, fingerprint);

                var resolved1 = NubPositionValidator.Resolve(saved, monitors1, nubLength);
                var resolved2 = NubPositionValidator.Resolve(saved, monitors2, nubLength);

                // Each resolved state must use the matched monitor's work area.
                bool usesWorkArea1 = resolved1.WorkArea == workArea1;
                bool usesWorkArea2 = resolved2.WorkArea == workArea2;

                return usesWorkArea1 && usesWorkArea2;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
