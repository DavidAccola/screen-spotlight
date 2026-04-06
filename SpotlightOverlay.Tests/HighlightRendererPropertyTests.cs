using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Rendering;
using Xunit;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: highlight-tool
///
/// Property 4: HighlightRenderer produces filled rects with no stroke
/// Validates: Requirements 8.1, 8.4
///
/// Property 5: HighlightRenderer degenerate rect guard
/// Validates: Requirements 8.3
///
/// Property 6: HighlightRenderer accumulation and clear round-trip
/// Validates: Requirements 8.2
/// </summary>
public class HighlightRendererPropertyTests
{
    private static Gen<Rect> ValidRectGen =>
        from x in Gen.Choose(0, 1000).Select(v => (double)v)
        from y in Gen.Choose(0, 1000).Select(v => (double)v)
        from w in Gen.Choose(2, 500).Select(v => (double)v)
        from h in Gen.Choose(2, 500).Select(v => (double)v)
        select new Rect(x, y, w, h);

    private static Gen<Rect> DegenerateRectGen =>
        Gen.OneOf(
            from x in Gen.Choose(0, 1000).Select(v => (double)v)
            from y in Gen.Choose(0, 1000).Select(v => (double)v)
            from w in Gen.Choose(0, 1).Select(v => (double)v)
            from h in Gen.Choose(2, 500).Select(v => (double)v)
            select new Rect(x, y, w, h),
            from x in Gen.Choose(0, 1000).Select(v => (double)v)
            from y in Gen.Choose(0, 1000).Select(v => (double)v)
            from w in Gen.Choose(2, 500).Select(v => (double)v)
            from h in Gen.Choose(0, 1).Select(v => (double)v)
            select new Rect(x, y, w, h)
        );

    private static Gen<Color> ColorGen =>
        from r in Gen.Choose(0, 255)
        from g in Gen.Choose(0, 255)
        from b in Gen.Choose(0, 255)
        select Color.FromRgb((byte)r, (byte)g, (byte)b);

    /// <summary>
    /// Property 4: BuildHighlightPath returns a filled rect with no stroke.
    /// Fill color must match the supplied color (full alpha). Stroke must be null.
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildHighlightPath_ProducesFilledRect_WithNoStroke()
    {
        if (!StaHelper.CanRunWpf) return;

        var prop = Prop.ForAll(
            ValidRectGen.ToArbitrary(),
            ColorGen.ToArbitrary(),
            (rect, color) =>
            {
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new HighlightRenderer();
                    var element = renderer.BuildHighlightPath(rect, color);

                    Assert.NotNull(element);
                    var rectangle = Assert.IsType<Rectangle>(element);

                    // Stroke must be null
                    Assert.Null(rectangle.Stroke);

                    // Fill must be a SolidColorBrush with the supplied color at full alpha
                    var fillBrush = Assert.IsType<SolidColorBrush>(rectangle.Fill);
                    Assert.Equal(color.R, fillBrush.Color.R);
                    Assert.Equal(color.G, fillBrush.Color.G);
                    Assert.Equal(color.B, fillBrush.Color.B);
                    Assert.Equal(0xFF, fillBrush.Color.A);

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 5: BuildHighlightPath returns null for degenerate rects (width or height <= 1).
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildHighlightPath_ReturnsNull_ForDegenerateRects()
    {
        if (!StaHelper.CanRunWpf) return;

        var prop = Prop.ForAll(
            DegenerateRectGen.ToArbitrary(),
            ColorGen.ToArbitrary(),
            (rect, color) =>
            {
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new HighlightRenderer();
                    var element = renderer.BuildHighlightPath(rect, color);
                    Assert.Null(element);
                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 6: AddHighlight accumulates correctly; ClearHighlights resets to zero.
    /// </summary>
    [Property(MaxTest = 100)]
    public void AddHighlight_Accumulates_And_ClearHighlights_ResetsToZero()
    {
        var prop = Prop.ForAll(
            ValidRectGen.ListOf().ToArbitrary(),
            rects =>
            {
                var renderer = new HighlightRenderer();

                foreach (var rect in rects)
                    renderer.AddHighlight(rect);

                if (renderer.HighlightCount != rects.Count)
                    return false;

                renderer.ClearHighlights();

                return renderer.HighlightCount == 0;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
