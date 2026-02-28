using WinFormsScreen = System.Windows.Forms.Screen;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Provides multi-monitor identification and coordinate translation utilities.
/// </summary>
public static class MonitorHelper
{
    /// <summary>
    /// Returns the bounds of the monitor containing the given screen point.
    /// Falls back to the primary monitor if no monitor contains the point.
    /// </summary>
    public static System.Windows.Rect GetMonitorBounds(System.Windows.Point screenPoint)
    {
        var target = WinFormsScreen.AllScreens
            .FirstOrDefault(s => s.Bounds.Contains((int)screenPoint.X, (int)screenPoint.Y))
            ?? WinFormsScreen.PrimaryScreen!;

        var b = target.Bounds;
        return new System.Windows.Rect(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>
    /// Translates a screen-space rectangle to window-relative coordinates
    /// by subtracting the monitor's top-left offset.
    /// </summary>
    public static System.Windows.Rect ScreenToWindow(System.Windows.Rect screenRect, System.Windows.Point monitorTopLeft)
    {
        return new System.Windows.Rect(
            screenRect.X - monitorTopLeft.X,
            screenRect.Y - monitorTopLeft.Y,
            screenRect.Width,
            screenRect.Height);
    }
}
