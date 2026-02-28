using System.Windows;
using System.Windows.Media;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Windows;
using Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 4: Overlay matches monitor bounds and opacity
/// Validates: Requirements 3.3, 3.4, 7.2
///
/// For any monitor bounds rectangle and any valid Overlay_Opacity value, the created
/// OverlayWindow should have position and size equal to the monitor bounds, and its
/// background opacity should equal the configured Overlay_Opacity.
/// </summary>
public class OverlayWindowPropertyTests
{
    private static Gen<Rect> MonitorBoundsGen =>
        from x in Gen.Choose(-3840, 3840).Select(v => (double)v)
        from y in Gen.Choose(-2160, 2160).Select(v => (double)v)
        from w in Gen.Choose(800, 3840).Select(v => (double)v)
        from h in Gen.Choose(600, 2160).Select(v => (double)v)
        select new Rect(x, y, w, h);

    private static Gen<double> OpacityGen =>
        Gen.Choose(0, 100).Select(v => v / 100.0);

    /// <summary>
    /// **Validates: Requirements 3.3, 3.4, 7.2**
    /// Runs on a single STA thread to avoid exhausting Win32 Dispatcher resources.
    /// </summary>
    [Fact]
    public void Overlay_Matches_Monitor_Bounds_And_Opacity()
    {
        StaHelper.Run(() =>
        {
            var prop = Prop.ForAll(
                MonitorBoundsGen.ToArbitrary(),
                OpacityGen.ToArbitrary(),
                (bounds, opacity) =>
                {
                    var window = new OverlayWindow(bounds, opacity);
                    try
                    {
                        const double tolerance = 0.001;

                        bool leftMatch = Math.Abs(window.Left - bounds.Left) < tolerance;
                        bool topMatch = Math.Abs(window.Top - bounds.Top) < tolerance;
                        bool widthMatch = Math.Abs(window.Width - bounds.Width) < tolerance;
                        bool heightMatch = Math.Abs(window.Height - bounds.Height) < tolerance;

                        var grid = (System.Windows.Controls.Grid)window.Content;
                        var brush = (SolidColorBrush)grid.Background;
                        byte expectedAlpha = (byte)(opacity * 255);
                        bool opacityMatch = brush.Color.A == expectedAlpha;

                        return leftMatch && topMatch && widthMatch && heightMatch && opacityMatch;
                    }
                    finally
                    {
                        window.Close();
                    }
                });

            prop.QuickCheckThrowOnFailure();
        });
    }
}
