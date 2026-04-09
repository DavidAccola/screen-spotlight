using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: toolbar-nub-persistence
/// Property tests for MonitorHelper.BuildFingerprint.
/// </summary>
public class MonitorFingerprintPropertyTests
{
    // Feature: toolbar-nub-persistence, Property 4: MonitorFingerprint encodes device name and resolution
    /// <summary>
    /// **Validates: Requirements 1.3**
    /// For any non-empty device name string and any positive physical width and height,
    /// BuildFingerprint must return a string that contains the device name as a substring
    /// and contains the resolution in the form "{width}x{height}" as a substring.
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildFingerprint_ContainsDeviceNameAndResolution()
    {
        var gen =
            from deviceName in Arb.Generate<NonEmptyString>().Select(s => s.Get)
            from width in Gen.Choose(1, 10000)
            from height in Gen.Choose(1, 10000)
            select (deviceName, width, height);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (deviceName, width, height) = input;

                string fp = MonitorHelper.BuildFingerprint(deviceName, width, height);

                bool containsDeviceName = fp.Contains(deviceName);
                bool containsResolution = fp.Contains($"{width}x{height}");

                return containsDeviceName && containsResolution;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: toolbar-nub-persistence, Property 4 (overload): MonitorInfo overload delegates correctly
    /// <summary>
    /// **Validates: Requirements 1.3**
    /// BuildFingerprint(MonitorInfo) must produce the same result as
    /// BuildFingerprint(deviceName, physicalWidth, physicalHeight).
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildFingerprint_MonitorInfoOverload_MatchesStringOverload()
    {
        var gen =
            from deviceName in Arb.Generate<NonEmptyString>().Select(s => s.Get)
            from width in Gen.Choose(1, 10000)
            from height in Gen.Choose(1, 10000)
            select (deviceName, width, height);

        var prop = Prop.ForAll(
            gen.ToArbitrary(),
            input =>
            {
                var (deviceName, width, height) = input;

                var monitor = new MonitorInfo(
                    DeviceName: deviceName,
                    PhysicalWidth: width,
                    PhysicalHeight: height,
                    WorkArea: new System.Windows.Rect(0, 0, width, height),
                    IsPrimary: true);

                string fpFromStrings = MonitorHelper.BuildFingerprint(deviceName, width, height);
                string fpFromMonitor = MonitorHelper.BuildFingerprint(monitor);

                return fpFromStrings == fpFromMonitor;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
