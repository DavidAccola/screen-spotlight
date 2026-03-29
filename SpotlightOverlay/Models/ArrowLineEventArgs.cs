namespace SpotlightOverlay.Models;

/// <summary>
/// Event arguments emitted when an arrow drag gesture updates or completes.
/// Carries raw start/end points preserving direction.
/// </summary>
public class ArrowLineEventArgs : EventArgs
{
    /// <summary>The point where the drag started in screen coordinates.</summary>
    public System.Windows.Point StartPoint { get; }

    /// <summary>The current or final point of the drag in screen coordinates.</summary>
    public System.Windows.Point EndPoint { get; }

    public ArrowLineEventArgs(System.Windows.Point start, System.Windows.Point end)
    {
        StartPoint = start;
        EndPoint = end;
    }
}
