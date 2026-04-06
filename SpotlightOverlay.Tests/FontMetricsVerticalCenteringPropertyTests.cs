using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Rendering;
using Xunit;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: font-metrics-vertical-centering
///
/// Property 1: Bug Condition - Font Metrics Vertical Centering
/// Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3
///
/// Property 2: Preservation - Non-Vertical Positioning Behavior
/// Validates: Requirements 3.1, 3.2, 3.3, 3.4
/// </summary>
public class FontMetricsVerticalCenteringPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3**
    /// 
    /// Property 1: Bug Condition - Font Metrics Vertical Centering
    /// 
    /// For fonts with non-0.5 ascent/descent ratios (Times New Roman, Lucida Bright),
    /// the text MUST be vertically centered using font design metrics (Ascent/Descent)
    /// rather than rendered pixel measurements (ComputeInkBoundingBox).
    /// 
    /// This test MUST FAIL on unfixed code - failure confirms the bug exists.
    /// </summary>
    [Fact]
    public void BuildStepVisual_UsesFontMetrics_ForVerticalCentering()
    {
        if (!StaHelper.CanRunWpf) return;

        // Test with Times New Roman and Lucida Bright - fonts known to have
        // ascent/descent ratios that expose the bug
        var problematicFonts = new[] { "Times New Roman", "Lucida Bright" };
        var stepNumbers = new[] { 1, 2, 3, 99 };
        var fontSizes = new[] { 16.0, 20.0, 24.0 };

        foreach (var fontFamily in problematicFonts)
        {
            foreach (var stepNumber in stepNumbers)
            {
                foreach (var fontSize in fontSizes)
                {
                    StaHelper.Run(() =>
                    {
                        var renderer = new StepsRenderer();
                        var options = new StepsRenderOptions(
                            Shape: StepsShape.Circle,
                            OutlineEnabled: true,
                            Size: 40.0,
                            FillColor: Colors.Blue,
                            OutlineColor: Colors.White,
                            FontFamily: fontFamily,
                            FontSize: fontSize
                        );

                        var anchorPoint = new Point(100, 100);
                        var tailAngle = 0.0;

                        var visual = renderer.BuildStepVisual(anchorPoint, tailAngle, stepNumber, options);

                        Assert.NotNull(visual);
                        var canvas = Assert.IsType<Canvas>(visual);

                        // Find the TextBlock in the canvas
                        TextBlock? textBlock = null;
                        foreach (var child in canvas.Children)
                        {
                            if (child is TextBlock tb)
                            {
                                textBlock = tb;
                                break;
                            }
                        }

                        Assert.NotNull(textBlock);

                        // Get the typeface and verify GlyphTypeface is available
                        var typeface = new Typeface(
                            new FontFamily(fontFamily),
                            FontStyles.Normal,
                            FontWeights.Normal,
                            FontStretches.Normal
                        );

                        if (typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface))
                        {
                            // Calculate expected vertical position using font metrics
                            double canvasSize = options.Size + (options.Size * 0.6 + 2.0) * 2;
                            double circleCenter = canvasSize / 2;

                            // In WPF, GlyphTypeface.Baseline is the ascent (distance from top to baseline)
                            // and GlyphTypeface.Height is the total height (ascent + descent)
                            // So descent = Height - Baseline
                            double ascent = glyphTypeface.Baseline;
                            double descent = glyphTypeface.Height - glyphTypeface.Baseline;

                            // Expected optical center calculation
                            double opticalCenter = (ascent - descent) / 2 * fontSize;
                            double expectedTextTop = circleCenter - opticalCenter + (glyphTypeface.Baseline * fontSize);

                            // Get actual textTop from the canvas
                            double actualTextTop = Canvas.GetTop(textBlock);

                            // The text should be positioned using font metrics, not ink bounding box
                            // Allow small tolerance for floating point precision
                            Assert.Equal(expectedTextTop, actualTextTop, precision: 1);
                        }
                    });
                }
            }
        }
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// 
    /// Property 2: Preservation - Non-Vertical Positioning Behavior
    /// 
    /// For all rendering aspects that are NOT vertical text positioning (horizontal centering,
    /// shape rendering, outline rendering, canvas positioning, fallback mechanism), the fixed
    /// code SHALL produce exactly the same behavior as the original code.
    /// 
    /// This test MUST PASS on unfixed code - it establishes the baseline behavior to preserve.
    /// </summary>
    [Property(MaxTest = 50)]
    public void BuildStepVisual_PreservesHorizontalCentering()
    {
        if (!StaHelper.CanRunWpf) return;

        var testDataGen = 
            from fontFamily in Gen.Elements("Arial", "Times New Roman", "Courier New", "Verdana")
            from stepNumber in Gen.Choose(1, 99)
            from fontSize in Gen.Choose(12, 32).Select(v => (double)v)
            from size in Gen.Choose(30, 60).Select(v => (double)v)
            select (fontFamily, stepNumber, fontSize, size);

        var prop = Prop.ForAll(
            testDataGen.ToArbitrary(),
            data =>
            {
                var (fontFamily, stepNumber, fontSize, size) = data;
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new StepsRenderer();
                    var options = new StepsRenderOptions(
                        Shape: StepsShape.Circle,
                        OutlineEnabled: true,
                        Size: size,
                        FillColor: Colors.Blue,
                        OutlineColor: Colors.White,
                        FontFamily: fontFamily,
                        FontSize: fontSize
                    );

                    var anchorPoint = new Point(100, 100);
                    var tailAngle = 0.0;

                    var visual = renderer.BuildStepVisual(anchorPoint, tailAngle, stepNumber, options);

                    Assert.NotNull(visual);
                    var canvas = Assert.IsType<Canvas>(visual);

                    // Find the TextBlock in the canvas
                    TextBlock? textBlock = null;
                    foreach (var child in canvas.Children)
                    {
                        if (child is TextBlock tb)
                        {
                            textBlock = tb;
                            break;
                        }
                    }

                    Assert.NotNull(textBlock);

                    // Verify horizontal centering is preserved
                    // The text should be horizontally centered in the circle
                    double canvasSize = size + (size * 0.6 + 2.0) * 2;
                    double circleCenter = canvasSize / 2;

                    double textLeft = Canvas.GetLeft(textBlock);

                    // Horizontal centering should place text near the center
                    // (exact position depends on text width, but should be within reasonable bounds)
                    Assert.True(textLeft > 0 && textLeft < canvasSize, 
                        $"Text should be horizontally positioned within canvas bounds. textLeft={textLeft}, canvasSize={canvasSize}");

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// 
    /// Property 2: Preservation - Shape Rendering
    /// 
    /// Verifies that circle and teardrop shapes render correctly with proper geometry.
    /// </summary>
    [Property(MaxTest = 50)]
    public void BuildStepVisual_PreservesShapeRendering()
    {
        if (!StaHelper.CanRunWpf) return;

        var testDataGen =
            from shape in Gen.Elements(StepsShape.Circle, StepsShape.Teardrop)
            from size in Gen.Choose(30, 60).Select(v => (double)v)
            from outlineEnabled in Gen.Elements(true, false)
            select (shape, size, outlineEnabled);

        var prop = Prop.ForAll(
            testDataGen.ToArbitrary(),
            data =>
            {
                var (shape, size, outlineEnabled) = data;
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new StepsRenderer();
                    var options = new StepsRenderOptions(
                        Shape: shape,
                        OutlineEnabled: outlineEnabled,
                        Size: size,
                        FillColor: Colors.Blue,
                        OutlineColor: Colors.White,
                        FontFamily: "Arial",
                        FontSize: 16.0
                    );

                    var anchorPoint = new Point(100, 100);
                    var tailAngle = Math.PI / 4; // 45 degrees

                    var visual = renderer.BuildStepVisual(anchorPoint, tailAngle, 1, options);

                    Assert.NotNull(visual);
                    var canvas = Assert.IsType<Canvas>(visual);

                    // Find the Path (shape) in the canvas
                    System.Windows.Shapes.Path? path = null;
                    foreach (var child in canvas.Children)
                    {
                        if (child is System.Windows.Shapes.Path p)
                        {
                            path = p;
                            break;
                        }
                    }

                    Assert.NotNull(path);
                    Assert.NotNull(path.Data);

                    // Verify fill color
                    var fillBrush = Assert.IsType<SolidColorBrush>(path.Fill);
                    Assert.Equal(Colors.Blue, fillBrush.Color);

                    // Verify outline
                    if (outlineEnabled)
                    {
                        Assert.NotNull(path.Stroke);
                        var strokeBrush = Assert.IsType<SolidColorBrush>(path.Stroke);
                        Assert.Equal(Colors.White, strokeBrush.Color);
                        Assert.True(path.StrokeThickness > 0);
                    }

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// 
    /// Property 2: Preservation - Canvas Positioning
    /// 
    /// Verifies that canvas positioning remains correct relative to anchor point.
    /// </summary>
    [Property(MaxTest = 50)]
    public void BuildStepVisual_PreservesCanvasPositioning()
    {
        if (!StaHelper.CanRunWpf) return;

        var testDataGen =
            from anchorX in Gen.Choose(50, 500).Select(v => (double)v)
            from anchorY in Gen.Choose(50, 500).Select(v => (double)v)
            from size in Gen.Choose(30, 60).Select(v => (double)v)
            select (anchorX, anchorY, size);

        var prop = Prop.ForAll(
            testDataGen.ToArbitrary(),
            data =>
            {
                var (anchorX, anchorY, size) = data;
                bool result = false;
                StaHelper.Run(() =>
                {
                    var renderer = new StepsRenderer();
                    var options = new StepsRenderOptions(
                        Shape: StepsShape.Circle,
                        OutlineEnabled: true,
                        Size: size,
                        FillColor: Colors.Blue,
                        OutlineColor: Colors.White,
                        FontFamily: "Arial",
                        FontSize: 16.0
                    );

                    var anchorPoint = new Point(anchorX, anchorY);
                    var tailAngle = 0.0;

                    var visual = renderer.BuildStepVisual(anchorPoint, tailAngle, 1, options);

                    Assert.NotNull(visual);
                    var canvas = Assert.IsType<Canvas>(visual);

                    // Verify canvas has proper size
                    double expectedCanvasSize = size + (size * 0.6 + 2.0) * 2;
                    Assert.Equal(expectedCanvasSize, canvas.Width, precision: 1);
                    Assert.Equal(expectedCanvasSize, canvas.Height, precision: 1);

                    // Verify canvas is positioned so circle center aligns with anchor point
                    double canvasLeft = Canvas.GetLeft(canvas);
                    double canvasTop = Canvas.GetTop(canvas);

                    double circleCenter = expectedCanvasSize / 2;
                    double expectedCanvasLeft = anchorX - circleCenter;
                    double expectedCanvasTop = anchorY - circleCenter;

                    Assert.Equal(expectedCanvasLeft, canvasLeft, precision: 1);
                    Assert.Equal(expectedCanvasTop, canvasTop, precision: 1);

                    result = true;
                });
                return result;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
