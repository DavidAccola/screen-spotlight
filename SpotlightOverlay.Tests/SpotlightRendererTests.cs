using System.IO;
using System.Windows;
using System.Windows.Media;
using SpotlightOverlay.Rendering;
using SpotlightOverlay.Services;
using Xunit;
using Size = System.Windows.Size;

namespace SpotlightOverlay.Tests;

public class SpotlightRendererTests : IDisposable
{
    private readonly string _tempFilePath;
    private readonly SettingsService _settings;

    public SpotlightRendererTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        _settings = new SettingsService(_tempFilePath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
            File.Delete(_tempFilePath);
    }

    /// <summary>
    /// Validates: Requirement 4.2
    /// The SpotlightRenderer shall support a minimum of 10 simultaneous cutout regions.
    /// </summary>
    [Fact]
    public void Supports_10_Simultaneous_Cutouts()
    {
        if (!StaHelper.CanRunWpf) return; // WPF rendering not available in this environment

        StaHelper.Run(() =>
        {
            var renderer = new SpotlightRenderer(_settings);

            for (int i = 0; i < 10; i++)
                renderer.AddCutout(new Rect(i * 100, i * 50, 80, 60));

            Assert.Equal(10, renderer.CutoutCount);
            Assert.Equal(10, renderer.Cutouts.Count);

            var mask = renderer.BuildOpacityMask(new Size(1920, 1080));
            Assert.Equal(11, mask.Children.Count); // 1 background + 10 cutouts
        });
    }

    /// <summary>
    /// Validates: Requirement 6.4 (ClearCutouts resets state)
    /// </summary>
    [Fact]
    public void ClearCutouts_Resets_State()
    {
        var renderer = new SpotlightRenderer(_settings);
        renderer.AddCutout(new Rect(10, 20, 100, 80));
        renderer.AddCutout(new Rect(200, 300, 150, 120));
        Assert.Equal(2, renderer.CutoutCount);

        renderer.ClearCutouts();

        Assert.Equal(0, renderer.CutoutCount);
        Assert.Empty(renderer.Cutouts);
    }

    /// <summary>
    /// Validates: Requirement 5.1
    /// </summary>
    [Fact]
    public void Gradient_Uses_Correct_Brush_Types()
    {
        if (!StaHelper.CanRunWpf) return; // WPF rendering not available in this environment

        StaHelper.Run(() =>
        {
            _settings.FeatherRadius = 30;
            var renderer = new SpotlightRenderer(_settings);
            renderer.AddCutout(new Rect(100, 100, 200, 150));

            var mask = renderer.BuildOpacityMask(new Size(1920, 1080));

            var background = mask.Children[0] as GeometryDrawing;
            Assert.NotNull(background);
            Assert.IsType<SolidColorBrush>(background!.Brush);

            var cutoutDrawing = mask.Children[1] as GeometryDrawing;
            Assert.NotNull(cutoutDrawing);
            Assert.IsAssignableFrom<GradientBrush>(cutoutDrawing!.Brush);
        });
    }

    /// <summary>
    /// Validates: Requirement 5.4
    /// </summary>
    [Fact]
    public void Updated_FeatherRadius_Applies_To_New_Cutouts()
    {
        if (!StaHelper.CanRunWpf) return; // WPF rendering not available in this environment

        StaHelper.Run(() =>
        {
            var cutout = new Rect(100, 100, 200, 150);
            var overlaySize = new Size(1920, 1080);

            _settings.FeatherRadius = 20;
            var renderer1 = new SpotlightRenderer(_settings);
            renderer1.AddCutout(cutout);
            var mask1 = renderer1.BuildOpacityMask(overlaySize);
            var drawing1 = (GeometryDrawing)mask1.Children[1];
            var bounds1 = drawing1.Geometry.Bounds;

            _settings.FeatherRadius = 60;
            var renderer2 = new SpotlightRenderer(_settings);
            renderer2.AddCutout(cutout);
            var mask2 = renderer2.BuildOpacityMask(overlaySize);
            var drawing2 = (GeometryDrawing)mask2.Children[1];
            var bounds2 = drawing2.Geometry.Bounds;

            Assert.True(bounds2.Width > bounds1.Width);
            Assert.True(bounds2.Height > bounds1.Height);

            Assert.Equal(cutout.Width + 2 * 20, bounds1.Width);
            Assert.Equal(cutout.Height + 2 * 20, bounds1.Height);
            Assert.Equal(cutout.Width + 2 * 60, bounds2.Width);
            Assert.Equal(cutout.Height + 2 * 60, bounds2.Height);
        });
    }
}
