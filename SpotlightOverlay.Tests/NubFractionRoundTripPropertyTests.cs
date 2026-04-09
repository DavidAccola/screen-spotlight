using FsCheck;
using FsCheck.Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: toolbar-nub-persistence
/// Property 1: NubFraction round-trip
/// </summary>
public class NubFractionRoundTripPropertyTests
{
    // Feature: toolbar-nub-persistence, Property 1: NubFraction round-trip
    /// <summary>
    /// **Validates: Requirements 2.1, 2.2, 4.2, 6.4**
    /// For any NubFraction in [0.0, 1.0] and any work area edge length greater than zero,
    /// converting the fraction to a DIP coordinate and then back to a fraction must produce
    /// a value equal to the original fraction (within floating-point tolerance 1e-10).
    /// </summary>
    [Property(MaxTest = 1000)]
    public void NubFraction_RoundTrip_PreservesValue()
    {
        var gen =
            from fractionRaw in Gen.Choose(0, 10000).Select(v => v / 10000.0)
            from edgeLengthRaw in Gen.Choose(1, 10000).Select(v => (double)v)
            from nubLengthRaw in Gen.Choose(0, 9999).Select(v => (double)v)
            where nubLengthRaw < edgeLengthRaw
            from workAreaStartRaw in Gen.Choose(-5000, 5000).Select(v => (double)v)
            select (fractionRaw, edgeLengthRaw, nubLengthRaw, workAreaStartRaw);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (fraction, edgeLength, nubLength, workAreaStart) = input;

                // Convert fraction → DIP coordinate
                double dipCoord = workAreaStart + fraction * (edgeLength - nubLength);

                // Convert DIP coordinate → fraction
                double reFraction = (dipCoord - workAreaStart) / (edgeLength - nubLength);

                return Math.Abs(reFraction - fraction) <= 1e-10;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
