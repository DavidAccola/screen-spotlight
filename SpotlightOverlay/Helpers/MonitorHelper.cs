using System.Runtime.InteropServices;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Provides multi-monitor identification and coordinate translation utilities.
/// </summary>
public static class MonitorHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>
    /// Returns the physical-pixel bounds of the monitor containing the given screen point.
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
    /// Returns the DIP-converted bounds of the monitor containing the given screen point.
    /// WPF window Left/Top/Width/Height are in DIPs, so physical pixel bounds must be
    /// divided by the DPI scale factor to position/size the window correctly.
    /// </summary>
    public static System.Windows.Rect GetMonitorBoundsDip(System.Windows.Point screenPoint)
    {
        var physicalBounds = GetMonitorBounds(screenPoint);
        double scale = GetDpiScale(screenPoint);
        return new System.Windows.Rect(
            physicalBounds.X / scale,
            physicalBounds.Y / scale,
            physicalBounds.Width / scale,
            physicalBounds.Height / scale);
    }

    /// <summary>
    /// Returns the DPI scale factor (e.g. 1.25 for 125%) for the monitor at the given point.
    /// </summary>
    public static double GetDpiScale(System.Windows.Point screenPoint)
    {
        try
        {
            var pt = new POINT { x = (int)screenPoint.X, y = (int)screenPoint.Y };
            IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMonitor != IntPtr.Zero)
            {
                int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
                if (hr == 0 && dpiX > 0)
                    return dpiX / 96.0;
            }
        }
        catch { }
        return 1.0;
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

    /// <summary>
    /// Enumerates all connected monitors and returns a <see cref="MonitorInfo"/> snapshot for each.
    /// WorkArea is expressed in DIPs using the monitor's DPI scale.
    /// </summary>
    public static MonitorInfo[] GetAllMonitors()
    {
        return WinFormsScreen.AllScreens.Select(s => new MonitorInfo(
            DeviceName: s.DeviceName,
            PhysicalWidth: s.Bounds.Width,
            PhysicalHeight: s.Bounds.Height,
            WorkArea: DipRect(s.WorkingArea),
            IsPrimary: s.Primary
        )).ToArray();
    }

    /// <summary>
    /// Converts a physical-pixel <see cref="System.Drawing.Rectangle"/> (e.g. from WinForms Screen)
    /// to a DIP <see cref="System.Windows.Rect"/> using the DPI scale of the monitor at that rectangle's top-left corner.
    /// </summary>
    private static System.Windows.Rect DipRect(System.Drawing.Rectangle physicalRect)
    {
        var point = new System.Windows.Point(physicalRect.X, physicalRect.Y);
        double scale = GetDpiScale(point);
        return new System.Windows.Rect(
            physicalRect.X / scale,
            physicalRect.Y / scale,
            physicalRect.Width / scale,
            physicalRect.Height / scale);
    }

    /// <summary>
    /// Builds a stable fingerprint string for a monitor from its device name and physical resolution.
    /// Format: "{DeviceName}|{PhysicalWidth}x{PhysicalHeight}"
    /// </summary>
    public static string BuildFingerprint(string deviceName, int physicalWidth, int physicalHeight)
        => $"{deviceName}|{physicalWidth}x{physicalHeight}";

    /// <summary>
    /// Builds a stable fingerprint string for a <see cref="MonitorInfo"/>.
    /// </summary>
    public static string BuildFingerprint(MonitorInfo monitor)
        => BuildFingerprint(monitor.DeviceName, monitor.PhysicalWidth, monitor.PhysicalHeight);

    /// <summary>
    /// Returns the DIP work area of the monitor containing the given DIP point.
    /// Falls back to the nearest monitor if the point is between monitors (e.g. during drag).
    /// </summary>
    public static System.Windows.Rect GetWorkAreaForPoint(System.Windows.Point dipPoint)
    {
        var monitors = GetAllMonitors();
        foreach (var m in monitors)
            if (m.WorkArea.Contains(dipPoint))
                return m.WorkArea;

        // Point is outside all work areas (e.g. in a gap or on a taskbar) — find nearest
        MonitorInfo? nearest = null;
        double bestDist = double.MaxValue;
        foreach (var m in monitors)
        {
            double dist = DistanceToRect(dipPoint, m.WorkArea);
            if (dist < bestDist) { bestDist = dist; nearest = m; }
        }
        return nearest?.WorkArea ?? monitors[0].WorkArea;
    }

    /// <summary>
    /// Returns the full monitor bounds (including taskbar area) in DIPs for the monitor
    /// containing the given DIP point. Used for drag clamping where the nub should be
    /// draggable onto the taskbar area.
    /// </summary>
    public static System.Windows.Rect GetMonitorBoundsForPoint(System.Windows.Point dipPoint)
    {
        var screen = WinFormsScreen.AllScreens
            .FirstOrDefault(s =>
            {
                double scale = GetDpiScale(new System.Windows.Point(s.Bounds.X, s.Bounds.Y));
                var dipBounds = new System.Windows.Rect(
                    s.Bounds.X / scale, s.Bounds.Y / scale,
                    s.Bounds.Width / scale, s.Bounds.Height / scale);
                return dipBounds.Contains(dipPoint);
            });

        if (screen == null)
        {
            // Nearest screen by work area
            var workArea = GetWorkAreaForPoint(dipPoint);
            screen = WinFormsScreen.AllScreens
                .FirstOrDefault(s =>
                {
                    double scale = GetDpiScale(new System.Windows.Point(s.Bounds.X, s.Bounds.Y));
                    return Math.Abs(s.WorkingArea.X / scale - workArea.X) < 1;
                }) ?? WinFormsScreen.PrimaryScreen!;
        }

        double sc = GetDpiScale(new System.Windows.Point(screen.Bounds.X, screen.Bounds.Y));
        return new System.Windows.Rect(
            screen.Bounds.X / sc, screen.Bounds.Y / sc,
            screen.Bounds.Width / sc, screen.Bounds.Height / sc);
    }

    /// <summary>
    /// Returns the minimum distance from a point to a rectangle.
    /// Returns 0 if the point is inside the rectangle.
    /// </summary>
    private static double DistanceToRect(System.Windows.Point p, System.Windows.Rect r)
    {
        double dx = Math.Max(r.Left - p.X, Math.Max(0, p.X - r.Right));
        double dy = Math.Max(r.Top - p.Y, Math.Max(0, p.Y - r.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
