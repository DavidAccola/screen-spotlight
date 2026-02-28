using System.Windows;

namespace SpotlightOverlay.Models;

/// <summary>
/// Event arguments emitted when a Ctrl+Click+Drag gesture completes.
/// </summary>
public class DragRectEventArgs : EventArgs
{
    /// <summary>
    /// The drag rectangle in absolute screen coordinates.
    /// </summary>
    public Rect ScreenRect { get; }

    /// <summary>
    /// The point where the drag started, used for monitor identification.
    /// </summary>
    public System.Windows.Point DragStartPoint { get; }

    public DragRectEventArgs(Rect screenRect, System.Windows.Point dragStartPoint)
    {
        ScreenRect = screenRect;
        DragStartPoint = dragStartPoint;
    }
}
