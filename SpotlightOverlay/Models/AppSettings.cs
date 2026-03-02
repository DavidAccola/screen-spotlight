namespace SpotlightOverlay.Models;

/// <summary>
/// Immutable settings record for the Spotlight Overlay application.
/// </summary>
public record AppSettings(double OverlayOpacity, int FeatherRadius, PreviewStyle PreviewStyle, DragStyle DragStyle);

public enum PreviewStyle
{
    Outline = 0,
    Crosshair = 1
}

public enum DragStyle
{
    HoldDrag = 0,
    ClickClick = 1
}
