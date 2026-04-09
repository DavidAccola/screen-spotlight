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
    ArrowheadStyle ArrowheadStyle = ArrowheadStyle.None,
    ArrowheadStyle ArrowEndStyle = ArrowheadStyle.FilledTriangle,
    ArrowLineStyle ArrowLineStyle = ArrowLineStyle.Solid,
    string ArrowColor = "FF0000",
    double ArrowLeftEndSize = 16.0,
    double ArrowLineThickness = 3.0,
    double ArrowRightEndSize = 16.0,
    bool SyncArrowEndStyle = true,
    bool SyncArrowEndSize = true,
    string CustomColors = "",
    ModifierKey ToggleToolModifier = ModifierKey.CtrlShift,
    int ToggleToolKey = 0x02, // VK_RBUTTON (right click = non-dominant by default)
    FadeMode FadeMode = FadeMode.Immediately,
    string BoxColor = "00A651",
    double BoxLineThickness = 3.0,
    string HighlightColor = "FFC90E",
    double HighlightOpacity = 0.5,
    string StepsFontFamily = "MS Reference Sans Serif, Roboto, MS UI Gothic",
    double StepsFontSize = 14.0,
    StepsShape StepsShape = StepsShape.Teardrop,
    bool StepsOutlineEnabled = true,
    double StepsSize = 36.0,
    string StepsFillColor = "3F48CC",
    string StepsOutlineColor = "FFFFFF",
    bool StepsFontBold = true,
    string StepsFontColor = "FFFFFF",
    double? NubFraction = null,
    AnchorEdge NubAnchorEdge = AnchorEdge.Right,
    string NubMonitorFingerprint = "");

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

public enum FadeMode
{
    Immediately = 0,
    AfterSpotlight = 1
}
