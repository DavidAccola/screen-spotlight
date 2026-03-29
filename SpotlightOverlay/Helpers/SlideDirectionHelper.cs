using SpotlightOverlay.Models;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Pure static helper that maps an <see cref="AnchorEdge"/> to the translation
/// axis and sign used for expand/collapse slide animations.
/// </summary>
public static class SlideDirectionHelper
{
    /// <summary>
    /// Returns the translation axis and sign for expand/collapse animation.
    /// <list type="bullet">
    ///   <item>Left  → slides right to expand: <c>(true, +1)</c></item>
    ///   <item>Right → slides left  to expand: <c>(true, -1)</c></item>
    ///   <item>Top   → slides down  to expand: <c>(false, +1)</c></item>
    /// </list>
    /// </summary>
    /// <param name="edge">The anchor edge the toolbar is docked to.</param>
    /// <returns>
    /// A tuple where <c>isHorizontal</c> indicates the animation axis (true = X, false = Y)
    /// and <c>expandSign</c> indicates the direction of expansion (+1 or -1).
    /// </returns>
    public static (bool isHorizontal, double expandSign) GetSlideDirection(AnchorEdge edge)
    {
        return edge switch
        {
            AnchorEdge.Left  => (true, +1.0),
            AnchorEdge.Right => (true, -1.0),
            AnchorEdge.Top   => (false, +1.0),
            _ => (true, -1.0) // Default to Right behavior
        };
    }
}
