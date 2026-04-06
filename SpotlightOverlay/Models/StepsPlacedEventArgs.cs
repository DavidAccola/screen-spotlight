namespace SpotlightOverlay.Models;

/// <summary>
/// Event arguments emitted when a Steps tool marker is placed or drag-updated.
/// Both points are in physical screen pixels.
/// </summary>
public class StepsPlacedEventArgs : EventArgs
{
    /// <summary>The point where the drag started (anchor) in physical screen pixels.</summary>
    public System.Windows.Point AnchorPoint { get; }

    /// <summary>The point where the drag ended (release) in physical screen pixels.</summary>
    public System.Windows.Point ReleasePoint { get; }

    /// <summary>True when the full activation modifier combo is held (smooth rotation).
    /// False when only the mouse is held / first click placed (snap to 90°).</summary>
    public bool ModifierHeld { get; }

    public StepsPlacedEventArgs(System.Windows.Point anchorPoint, System.Windows.Point releasePoint, bool modifierHeld = true)
    {
        AnchorPoint = anchorPoint;
        ReleasePoint = releasePoint;
        ModifierHeld = modifierHeld;
    }
}
