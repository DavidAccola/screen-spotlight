using System.Runtime.InteropServices;
using System.Text;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Detects fullscreen windows using DPI-aware geometric comparison.
/// All internal calculations use physical pixels to ensure accuracy across different DPI scales.
/// </summary>
public static class FullscreenDetector
{
    #region P/Invoke Declarations

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // Overload for RECT output (DWMWA_EXTENDED_FRAME_BOUNDS)
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    #endregion

    #region Constants

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MONITOR_DEFAULTTONULL = 0;
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int TOLERANCE_PIXELS = 10; // Physical pixels

    #endregion

    /// <summary>
    /// Checks if any fullscreen window exists on the specified monitor.
    /// </summary>
    public static bool IsAnyFullscreenWindowOnMonitor(MonitorInfo monitor)
    {
        return FindFullscreenWindow(monitor) != null;
    }

    /// <summary>
    /// Finds the first fullscreen window on the specified monitor.
    /// Returns null if no fullscreen window is found.
    /// </summary>
    public static FullscreenWindowInfo? FindFullscreenWindow(MonitorInfo monitor)
    {
        FullscreenWindowInfo? result = null;

        // Get monitor physical bounds
        var monitorPhysical = GetMonitorPhysicalBounds(monitor);
        var workAreaPhysical = GetWorkAreaPhysicalBounds(monitor);
        var monitorHandle = GetMonitorHandle(monitor);

        DebugLog.Write($"[Fullscreen] Checking monitor: {monitor.DeviceName}");
        DebugLog.Write($"[Fullscreen]   Physical bounds: {RectToString(monitorPhysical)}");
        DebugLog.Write($"[Fullscreen]   Work area: {RectToString(workAreaPhysical)}");

        EnumWindows((hwnd, lParam) =>
        {
            try
            {
                // Skip invisible windows
                if (!IsWindowVisible(hwnd))
                    return true;

                // Skip transparent overlays
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TRANSPARENT) != 0)
                    return true;

                // Skip tool windows (like our toolbar)
                if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                    return true;

                // Skip cloaked windows (UWP apps that are hidden)
                if (IsCloaked(hwnd))
                    return true;

                // Get window title
                string title = GetWindowTitle(hwnd);

                // Skip known system windows
                if (IsSystemWindow(title))
                    return true;

                // Use DWMWA_EXTENDED_FRAME_BOUNDS to get the visible rendered rect.
                // GetWindowRect includes the invisible DWM drop-shadow border, so both maximized
                // and fullscreen Chromium windows report the same oversized rect.
                // EXTENDED_FRAME_BOUNDS strips the shadow and returns the actual visible area,
                // already in physical screen pixels — no DPI conversion needed.
                RECT frameRect;
                int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out frameRect, Marshal.SizeOf<RECT>());
                if (hr != 0)
                {
                    // DWM unavailable (e.g. exclusive fullscreen game) — fall back to GetWindowRect + DPI conversion
                    if (!GetWindowRect(hwnd, out RECT logicalRect))
                        return true;
                    frameRect = ConvertToPhysicalRect(hwnd, logicalRect);
                }

                // Check if window is on the target monitor using its visible frame
                var windowMonitor = MonitorFromRect(ref frameRect, MONITOR_DEFAULTTONULL);
                if (windowMonitor == IntPtr.Zero || windowMonitor != monitorHandle)
                    return true; // Different monitor

                // Check if visible frame covers the entire monitor
                bool coversMonitor =
                    frameRect.Left <= monitorPhysical.Left + TOLERANCE_PIXELS &&
                    frameRect.Top <= monitorPhysical.Top + TOLERANCE_PIXELS &&
                    frameRect.Right >= monitorPhysical.Right - TOLERANCE_PIXELS &&
                    frameRect.Bottom >= monitorPhysical.Bottom - TOLERANCE_PIXELS;

                // Check if visible frame covers the taskbar area.
                // Maximized windows stop at the work area bottom; fullscreen windows cover the taskbar.
                bool coversTaskbar =
                    frameRect.Height >= workAreaPhysical.Height + TOLERANCE_PIXELS;

                // Log potential candidates
                if (coversMonitor || (frameRect.Width > workAreaPhysical.Width && frameRect.Height > workAreaPhysical.Height))
                {
                    DebugLog.Write($"[Fullscreen] Candidate: '{title}' hwnd=0x{hwnd.ToInt64():X}");
                    DebugLog.Write($"[Fullscreen]   FrameRect: {RectToString(frameRect)}");
                    DebugLog.Write($"[Fullscreen]   CoversMonitor: {coversMonitor}, CoversTaskbar: {coversTaskbar}");
                }

                if (coversMonitor && coversTaskbar)
                {
                    DebugLog.Write($"[Fullscreen]   -> FULLSCREEN DETECTED!");
                    result = new FullscreenWindowInfo(
                        Handle: hwnd,
                        Title: title,
                        PhysicalRect: frameRect,
                        Dpi: GetDpiForWindow(hwnd)
                    );
                    return false; // Stop enumeration
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Fullscreen] Error checking window: {ex.Message}");
            }

            return true; // Continue enumeration
        }, IntPtr.Zero);

        if (result == null)
        {
            DebugLog.Write($"[Fullscreen] No fullscreen window found on {monitor.DeviceName}");
        }

        return result;
    }

    #region Helper Methods

    /// <summary>
    /// Converts a logical window rect to physical pixels using the window's DPI.
    /// </summary>
    private static RECT ConvertToPhysicalRect(IntPtr hwnd, RECT logicalRect)
    {
        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96; // Fallback to 96 DPI

        double scale = dpi / 96.0;

        return new RECT
        {
            Left = (int)(logicalRect.Left * scale),
            Top = (int)(logicalRect.Top * scale),
            Right = (int)(logicalRect.Right * scale),
            Bottom = (int)(logicalRect.Bottom * scale)
        };
    }

    /// <summary>
    /// Gets the physical pixel bounds of a monitor (including taskbar area).
    /// </summary>
    private static RECT GetMonitorPhysicalBounds(MonitorInfo monitor)
    {
        // Use WinForms Screen to get physical bounds
        var screen = System.Windows.Forms.Screen.AllScreens
            .FirstOrDefault(s => s.DeviceName == monitor.DeviceName);

        if (screen != null)
        {
            var bounds = screen.Bounds;
            return new RECT
            {
                Left = bounds.X,
                Top = bounds.Y,
                Right = bounds.X + bounds.Width,
                Bottom = bounds.Y + bounds.Height
            };
        }

        // Fallback: use physical dimensions from MonitorInfo
        return new RECT
        {
            Left = 0,
            Top = 0,
            Right = monitor.PhysicalWidth,
            Bottom = monitor.PhysicalHeight
        };
    }

    /// <summary>
    /// Gets the physical pixel bounds of the work area (excluding taskbar).
    /// </summary>
    private static RECT GetWorkAreaPhysicalBounds(MonitorInfo monitor)
    {
        var screen = System.Windows.Forms.Screen.AllScreens
            .FirstOrDefault(s => s.DeviceName == monitor.DeviceName);

        if (screen != null)
        {
            var workArea = screen.WorkingArea;
            return new RECT
            {
                Left = workArea.X,
                Top = workArea.Y,
                Right = workArea.X + workArea.Width,
                Bottom = workArea.Y + workArea.Height
            };
        }

        // Fallback: assume work area is slightly smaller than full bounds
        return new RECT
        {
            Left = 0,
            Top = 0,
            Right = monitor.PhysicalWidth,
            Bottom = monitor.PhysicalHeight - 40 // Approximate taskbar height
        };
    }

    /// <summary>
    /// Gets the monitor handle for the specified monitor.
    /// </summary>
    private static IntPtr GetMonitorHandle(MonitorInfo monitor)
    {
        var bounds = GetMonitorPhysicalBounds(monitor);
        return MonitorFromRect(ref bounds, MONITOR_DEFAULTTONULL);
    }

    /// <summary>
    /// Checks if a window is cloaked (hidden by DWM, common for UWP apps).
    /// </summary>
    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            int cloaked = 0;
            int result = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
            return result == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the window title.
    /// </summary>
    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Checks if a window title indicates a system window that should be ignored.
    /// </summary>
    private static bool IsSystemWindow(string title)
    {
        return title == "Windows Input Experience" ||
               title == "Windows Shell Experience Host" ||
               title == "Program Manager" ||
               title == "Microsoft Text Input Application" ||
               title.Contains("- Kiro") || // Kiro IDE
               string.IsNullOrWhiteSpace(title);
    }

    /// <summary>
    /// Formats a RECT for logging.
    /// </summary>
    private static string RectToString(RECT rect)
    {
        return $"({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) [{rect.Width}×{rect.Height}]";
    }

    #endregion
}

/// <summary>
/// Information about a detected fullscreen window.
/// </summary>
public record FullscreenWindowInfo(
    IntPtr Handle,
    string Title,
    FullscreenDetector.RECT PhysicalRect,
    uint Dpi
);
