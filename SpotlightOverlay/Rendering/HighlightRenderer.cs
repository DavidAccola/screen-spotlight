using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SpotlightOverlay.Rendering;

/// <summary>
/// Stores highlight annotation data and builds WPF filled Rectangle visuals.
/// No shadow — highlights are flat fills. Opacity is controlled at the canvas level
/// (HighlightCanvas.Opacity) so overlapping rects don't accumulate alpha.
/// </summary>
public class HighlightRenderer
{
    private const double MinSize = 1.0; // degenerate threshold in DIPs

    private readonly List<Rect> _highlights = new();
    public int HighlightCount => _highlights.Count;
    public IReadOnlyList<Rect> Highlights => _highlights.AsReadOnly();

    public void AddHighlight(Rect rect) => _highlights.Add(rect);

    public void ClearHighlights() => _highlights.Clear();

    /// <summary>
    /// Builds a solid filled rectangle for the given rect.
    /// Returns null if the rect is degenerate (width or height &lt;= 1 DIP).
    /// Opacity is NOT set here — it is controlled by the containing HighlightCanvas.
    /// </summary>
    public FrameworkElement? BuildHighlightPath(Rect rect, Color color)
    {
        if (rect.Width <= MinSize || rect.Height <= MinSize) return null;

        var rectangle = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = new SolidColorBrush(Color.FromArgb(0xFF, color.R, color.G, color.B)),
            Stroke = null,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(rectangle, rect.X);
        Canvas.SetTop(rectangle, rect.Y);

        return rectangle;
    }
}
