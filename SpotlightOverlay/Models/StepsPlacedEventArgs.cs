namespace SpotlightOverlay.Models;

/// <summary>
/// Event arguments emitted when a Steps tool marker is placed.
/// Both points are in physical screen pixels.
/// </summary>
public class StepsPlacedEventArgs : EventArgs
{
    /// <summary>The point where the drag started (anchor) in physical screen pixels.</summary>
    public System.Windows.Point AnchorPoint { get; }

    /// <summary>The point where the drag ended (release) in physical screen pixels.</summary>
    public System.Windows.Point ReleasePoint { get; }

    public StepsPlacedEventArgs(System.Windows.Point anchorPoint, System.Windows.Point releasePoint)
    {
        AnchorPoint = anchorPoint;
        ReleasePoint = releasePoint;
    }
}
