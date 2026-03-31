namespace SpotlightOverlay.Models;

/// <summary>
/// Visual style of an arrow endpoint (start or end).
/// </summary>
public enum ArrowheadStyle
{
    None = 0,
    FilledTriangle = 1,
    OpenArrowhead = 2,
    Barbed = 3,
    DotEnd = 4
}

/// <summary>
/// Line dash style for the arrow shaft.
/// </summary>
public enum ArrowLineStyle
{
    Solid = 0,
    Dashed = 1,
    Dotted = 2
}
