using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SpotlightOverlay.Rendering;

/// <summary>
/// Stores box annotation data and builds WPF Rectangle visuals for rendering.
/// Mirrors ArrowRenderer structure — pure rendering logic, no window dependencies.
/// </summary>
public class BoxRenderer
{
    private const double ShadowOffset = 1.0;
    private const double MinSize = 1.0; // degenerate threshold in DIPs

    private static readonly Color ShadowColor = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);

    private readonly List<Rect> _boxes = new();
    public int BoxCount => _boxes.Count;
    public IReadOnlyList<Rect> Boxes => _boxes.AsReadOnly();

    public void AddBox(Rect rect) => _boxes.Add(rect);

    public void ClearBoxes() => _boxes.Clear();
    public void RemoveLastBox() { if (_boxes.Count > 0) _boxes.RemoveAt(_boxes.Count - 1); }

    /// <summary>
    /// Builds an unfilled rectangle stroke for the given rect.
    /// Returns null if the rect is degenerate (width or height &lt;= 1 DIP).
    /// </summary>
    public FrameworkElement? BuildBoxPath(Rect rect, Color color, double lineThickness)
    {
        if (rect.Width <= MinSize || rect.Height <= MinSize) return null;

        var rectangle = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = null,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = lineThickness,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(rectangle, rect.X);
        Canvas.SetTop(rectangle, rect.Y);

        return rectangle;
    }

    /// <summary>
    /// Builds a drop shadow rectangle offset by 1.0 DIP in both X and Y.
    /// Returns null if the rect is degenerate (width or height &lt;= 1 DIP).
    /// </summary>
    public FrameworkElement? BuildShadowPath(Rect rect, double lineThickness)
    {
        if (rect.Width <= MinSize || rect.Height <= MinSize) return null;

        var rectangle = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = null,
            Stroke = new SolidColorBrush(ShadowColor),
            StrokeThickness = lineThickness,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(rectangle, rect.X + ShadowOffset);
        Canvas.SetTop(rectangle, rect.Y + ShadowOffset);

        return rectangle;
    }
}
