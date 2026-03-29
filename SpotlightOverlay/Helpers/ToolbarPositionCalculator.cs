using System.Windows;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Immutable result of toolbar position calculation.
/// </summary>
public record ToolbarPosition(
    double Left,
    double Top,
    double WindowWidth,
    double WindowHeight);

/// <summary>
/// Pure static calculator for flyout toolbar window position based on anchor edge and work area.
/// Extracted as a pure function for property-based testing without WPF window instantiation.
/// </summary>
public static class ToolbarPositionCalculator
{
    /// <summary>
    /// Calculates the toolbar window position and size for the given anchor edge and work area.
    /// The window is always sized to the full toolbar dimensions (toolbarWidth x toolbarHeight).
    /// Animation handles showing/hiding content via TranslateTransform.
    /// </summary>
    /// <param name="edge">The screen edge to dock against.</param>
    /// <param name="workArea">The primary monitor work area in DIPs.</param>
    /// <param name="toolbarWidth">Full toolbar width (or height when Top-anchored).</param>
    /// <param name="toolbarHeight">Full toolbar height (or width when Top-anchored).</param>
    /// <param name="handleThickness">Drag handle narrow dimension (reserved for future use).</param>
    /// <returns>A <see cref="ToolbarPosition"/> with Left, Top, WindowWidth, WindowHeight.</returns>
    public static ToolbarPosition Calculate(
        AnchorEdge edge,
        Rect workArea,
        double toolbarWidth,
        double toolbarHeight,
        double handleThickness)
    {
        return edge switch
        {
            AnchorEdge.Left => new ToolbarPosition(
                Left: workArea.Left,
                Top: workArea.Top + (workArea.Height - toolbarHeight) / 2,
                WindowWidth: toolbarWidth,
                WindowHeight: toolbarHeight),

            AnchorEdge.Right => new ToolbarPosition(
                Left: workArea.Right - toolbarWidth,
                Top: workArea.Top + (workArea.Height - toolbarHeight) / 2,
                WindowWidth: toolbarWidth,
                WindowHeight: toolbarHeight),

            AnchorEdge.Top => new ToolbarPosition(
                Left: workArea.Left + (workArea.Width - toolbarWidth) / 2,
                Top: workArea.Top,
                WindowWidth: toolbarWidth,
                WindowHeight: toolbarHeight),

            _ => new ToolbarPosition(
                Left: workArea.Right - toolbarWidth,
                Top: workArea.Top + (workArea.Height - toolbarHeight) / 2,
                WindowWidth: toolbarWidth,
                WindowHeight: toolbarHeight)
        };
    }
}
