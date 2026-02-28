using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 8: Monitor identification from point
/// Validates: Requirements 7.1
///
/// For any set of non-overlapping monitor bounds and any point that lies within
/// one of those bounds, the monitor identification function should return the
/// monitor whose bounds contain that point.
/// </summary>
public class MonitorIdentificationPropertyTests
{
    /// <summary>
    /// Generates a random point within the bounds of one of the actual system monitors.
    /// </summary>
    private static Gen<System.Windows.Point> PointWithinAnyMonitorGen
    {
        get
        {
            var screens = WinFormsScreen.AllScreens;
            return Gen.Elements(screens).SelectMany(screen =>
            {
                var b = screen.Bounds;
                return from x in Gen.Choose(b.Left, b.Right - 1).Select(v => (double)v)
                       from y in Gen.Choose(b.Top, b.Bottom - 1).Select(v => (double)v)
                       select new System.Windows.Point(x, y);
            });
        }
    }

    // Feature: spotlight-overlay, Property 8: Monitor identification from point
    /// <summary>
    /// **Validates: Requirements 7.1**
    /// For any point within the actual system monitor bounds, GetMonitorBounds
    /// should return a rectangle that contains that point.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Monitor_Identification_Returns_Bounds_Containing_Point()
    {
        var prop = Prop.ForAll(
            PointWithinAnyMonitorGen.ToArbitrary(),
            (point) =>
            {
                var bounds = MonitorHelper.GetMonitorBounds(point);

                // The returned bounds must contain the generated point
                bool containsPoint = bounds.Contains(point);

                // The returned bounds must match one of the actual monitors
                bool matchesRealMonitor = WinFormsScreen.AllScreens.Any(s =>
                {
                    var b = s.Bounds;
                    var monitorRect = new System.Windows.Rect(b.X, b.Y, b.Width, b.Height);
                    return monitorRect == bounds;
                });

                return containsPoint && matchesRealMonitor;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
