namespace SpotlightOverlay.Models;

/// <summary>
/// Immutable settings record for the Spotlight Overlay application.
/// </summary>
public record AppSettings(
    double OverlayOpacity,
    int FeatherRadius,
    PreviewStyle PreviewStyle,
    DragStyle DragStyle,
    bool FreezeScreen,
    ModifierKey ActivationModifier,
    int ActivationKey,
    ModifierKey ToggleModifier,
    int ToggleKey,
    bool CumulativeSpotlights = true,
    AnchorEdge ToolbarAnchorEdge = AnchorEdge.Right,
    bool FlyoutToolbarVisible = true,
    ArrowheadStyle ArrowheadStyle = ArrowheadStyle.FilledTriangle,
    string ArrowColor = "FFFFFF");

public enum PreviewStyle
{
    Outline = 0,
    Crosshair = 1,
    Corners = 2
}

public enum DragStyle
{
    HoldDrag = 0,
    ClickClick = 1
}

/// <summary>
/// Modifier key(s) that activate the spotlight when held + click.
/// </summary>
public enum ModifierKey
{
    Ctrl = 0,
    Alt = 1,
    Shift = 2,
    CtrlShift = 3,
    CtrlAlt = 4,
    None = 5
}

/// <summary>
/// Screen edge to which the flyout toolbar is anchored.
/// </summary>
public enum AnchorEdge
{
    Left = 0,
    Right = 1,
    Top = 2
}
