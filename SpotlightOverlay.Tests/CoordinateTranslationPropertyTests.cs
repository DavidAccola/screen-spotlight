using System.Windows;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Helpers;
using Point = System.Windows.Point;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 9: Screen-to-window coordinate translation
/// Validates: Requirements 7.3
///
/// For any screen-space rectangle and any monitor offset (top-left corner of the
/// target monitor), subtracting the monitor offset from the rectangle coordinates
/// should produce window-relative coordinates, and adding the offset back should
/// recover the original screen coordinates (round-trip).
/// </summary>
public class CoordinateTranslationPropertyTests
{
    private static Gen<Rect> ScreenRectGen =>
        from x in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from y in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from w in Gen.Choose(1, 3000).Select(v => (double)v)
        from h in Gen.Choose(1, 3000).Select(v => (double)v)
        select new Rect(x, y, w, h);

    private static Gen<Point> MonitorOffsetGen =>
        from x in Gen.Choose(-5000, 5000).Select(v => (double)v)
        from y in Gen.Choose(-5000, 5000).Select(v => (double)v)
        select new Point(x, y);

    // Feature: spotlight-overlay, Property 9: Screen-to-window coordinate translation
    /// <summary>
    /// **Validates: Requirements 7.3**
    /// For any screen-space rectangle and monitor offset, ScreenToWindow should
    /// produce correct window-relative coordinates and the round-trip should
    /// recover the original screen coordinates.
    /// </summary>
    [Property(MaxTest = 100)]
    public void ScreenToWindow_RoundTrip_Recovers_Original_Coordinates()
    {
        var prop = Prop.ForAll(
            ScreenRectGen.ToArbitrary(),
            MonitorOffsetGen.ToArbitrary(),
            (screenRect, monitorTopLeft) =>
            {
                var windowRect = MonitorHelper.ScreenToWindow(screenRect, monitorTopLeft);

                // Verify window-relative coordinates are correct
                const double tolerance = 0.001;
                bool xCorrect = Math.Abs(windowRect.X - (screenRect.X - monitorTopLeft.X)) < tolerance;
                bool yCorrect = Math.Abs(windowRect.Y - (screenRect.Y - monitorTopLeft.Y)) < tolerance;
                bool widthPreserved = Math.Abs(windowRect.Width - screenRect.Width) < tolerance;
                bool heightPreserved = Math.Abs(windowRect.Height - screenRect.Height) < tolerance;

                // Verify round-trip: adding offset back recovers original
                double recoveredX = windowRect.X + monitorTopLeft.X;
                double recoveredY = windowRect.Y + monitorTopLeft.Y;
                bool roundTripX = Math.Abs(recoveredX - screenRect.X) < tolerance;
                bool roundTripY = Math.Abs(recoveredY - screenRect.Y) < tolerance;

                return xCorrect && yCorrect && widthPreserved && heightPreserved
                    && roundTripX && roundTripY;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
