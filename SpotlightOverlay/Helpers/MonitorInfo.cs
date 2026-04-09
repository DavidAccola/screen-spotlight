using System.Windows;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Plain data snapshot of a single monitor, used by NubPositionValidator.
/// No WPF or WinForms dependency — all values are in DIPs.
/// </summary>
public record MonitorInfo(
    string DeviceName,       // e.g. "\\.\DISPLAY1"
    int PhysicalWidth,       // physical pixels, used for fingerprint
    int PhysicalHeight,
    Rect WorkArea,           // DIP work area (excludes taskbar)
    bool IsPrimary);
