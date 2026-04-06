using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FsCheck;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;
using FsCheck.Xunit;
using SpotlightOverlay.Rendering;
using Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: box-tool
///
/// Property 4: BoxRenderer produces unfilled strokes
/// Validates: Requirements 5.2, 5.3, 6.2, 6.3
///
/// Property 5: BoxRenderer shadow offset and color
/// Validates: Requirements 5.4, 6.5, 8.5
///
/// Property 6: BoxRenderer degenerate rect guard
/// Validates: Requirements 8.4
///
/// Property 7: BoxRenderer accumulation and clear round-trip
/// Validates: Requirements 6.6, 6.7, 8.3
/// </summary>
public class BoxRendererPropertyTests
{
    // Generates valid rects (width > 1, height > 1)
    private static Gen<Rect> ValidRectGen =>
        from x in Gen.Choose(0, 1000).Select(v => (double)v)
        from y in Gen.Choose(0, 1000).Select(v => (double)v)
        from w in Gen.Choose(2, 500).Select(v => (double)v)
        from h in Gen.Choose(2, 500).Select(v => (double)v)
        select new Rect(x, y, w, h);

    // Generates degenerate rects (width <= 1 OR height <= 1)
    private static Gen<Rect> DegenerateRectGen =>
        Gen.OneOf(
            // width <= 1
            from x in Gen.Choose(0, 1000).Select(v => (double)v)
            from y in Gen.Choose(0, 1000).Select(v => (double)v)
            from w in Gen.Choose(0, 1).Select(v => (double)v)
            from h in Gen.Choose(2, 500).Select(v => (double)v)
            select new Rect(x, y, w, h),
            // height <= 1
            from x in Gen.Choose(0, 1000).Select(v => (double)v)
            from y in Gen.Choose(0, 1000).Select(v => (double)v)
            from w in Gen.Choose(2, 500).Select(v => (double)v)
            from h in Gen.Choose(0, 1).Select(v => (double)v)
            select new Rect(x, y, w, h)
        );

    private static Gen<Color> ColorGen =>
        from a in Gen.Choose(0, 255)
        from r in Gen.Choose(0, 255)
        from g in Gen.Choose(0, 255)
        from b in Gen.Choose(0, 255)
        select Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);

    private static Gen<double> ThicknessGen =>
        Gen.Choose(1, 12).Select(v => (double)v);

    /// <summary>
    /// Property 4: BuildBoxPath returns an element with no fill and the correct stroke color.
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildBoxPath_ProducesUnfilledStroke_WithCorrectColor()
    {
        if (!StaHelper.CanRunWpf) return;

        var prop = Prop.ForAll(
            ValidRectGen.ToArbitrary(),
            ColorGen.ToArbitrary(),
            ThicknessGen.ToArbitrary(),
            (rect, color, thickness) =>
            {
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new BoxRenderer();
                    var element = renderer.BuildBoxPath(rect, color, thickness);

                    Assert.NotNull(element);
                    var rectangle = Assert.IsType<Rectangle>(element);

                    // Fill must be null or transparent
                    bool fillIsEmpty = rectangle.Fill == null
                        || rectangle.Fill == Brushes.Transparent
                        || (rectangle.Fill is SolidColorBrush scb && scb.Color.A == 0);
                    Assert.True(fillIsEmpty, "Fill should be null or transparent");

                    // Stroke must match the supplied color
                    var strokeBrush = Assert.IsType<SolidColorBrush>(rectangle.Stroke);
                    Assert.Equal(color, strokeBrush.Color);

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 5: BuildShadowPath returns an element offset by (1.0, 1.0) DIP with stroke #CC000000.
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildShadowPath_HasCorrectOffsetAndColor()
    {
        if (!StaHelper.CanRunWpf) return;

        var prop = Prop.ForAll(
            ValidRectGen.ToArbitrary(),
            ThicknessGen.ToArbitrary(),
            (rect, thickness) =>
            {
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new BoxRenderer();
                    var element = renderer.BuildShadowPath(rect, thickness);

                    Assert.NotNull(element);
                    var rectangle = Assert.IsType<Rectangle>(element);

                    // Position must be offset by exactly (1.0, 1.0)
                    double left = Canvas.GetLeft(rectangle);
                    double top = Canvas.GetTop(rectangle);
                    Assert.Equal(rect.X + 1.0, left, precision: 5);
                    Assert.Equal(rect.Y + 1.0, top, precision: 5);

                    // Stroke must be #CC000000
                    var strokeBrush = Assert.IsType<SolidColorBrush>(rectangle.Stroke);
                    Assert.Equal(0xCC, strokeBrush.Color.A);
                    Assert.Equal(0x00, strokeBrush.Color.R);
                    Assert.Equal(0x00, strokeBrush.Color.G);
                    Assert.Equal(0x00, strokeBrush.Color.B);

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 6: Both build methods return null for degenerate rects (width or height <= 1).
    /// </summary>
    [Property(MaxTest = 100)]
    public void BuildMethods_ReturnNull_ForDegenerateRects()
    {
        if (!StaHelper.CanRunWpf) return;

        var prop = Prop.ForAll(
            DegenerateRectGen.ToArbitrary(),
            ColorGen.ToArbitrary(),
            ThicknessGen.ToArbitrary(),
            (rect, color, thickness) =>
            {
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new BoxRenderer();
                    var boxPath = renderer.BuildBoxPath(rect, color, thickness);
                    var shadowPath = renderer.BuildShadowPath(rect, thickness);

                    Assert.Null(boxPath);
                    Assert.Null(shadowPath);

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 7: AddBox accumulates correctly; ClearBoxes resets to zero.
    /// </summary>
    [Property(MaxTest = 100)]
    public void AddBox_Accumulates_And_ClearBoxes_ResetsToZero()
    {
        var prop = Prop.ForAll(
            ValidRectGen.ListOf().ToArbitrary(),
            rects =>
            {
                var renderer = new BoxRenderer();

                foreach (var rect in rects)
                    renderer.AddBox(rect);

                if (renderer.BoxCount != rects.Count)
                    return false;

                renderer.ClearBoxes();

                return renderer.BoxCount == 0;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
