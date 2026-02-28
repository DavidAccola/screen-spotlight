using System.IO;
using System.Windows;
using System.Windows.Media;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Rendering;
using SpotlightOverlay.Services;
using Xunit;
using Size = System.Windows.Size;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 7: Feather radius controls gradient extent
/// Validates: Requirements 5.2
///
/// For any cutout rectangle and any non-negative feather radius, the gradient brush
/// generated for that cutout should have its transition region width equal to the
/// configured feather radius value.
/// </summary>
public class FeatherRadiusPropertyTests : IDisposable
{
    private readonly string _tempFilePath;
    private readonly SettingsService _settings;

    public FeatherRadiusPropertyTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        _settings = new SettingsService(_tempFilePath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
            File.Delete(_tempFilePath);
    }

    private static Gen<Rect> CutoutRectGen =>
        from x in Gen.Choose(50, 1500).Select(v => (double)v)
        from y in Gen.Choose(50, 1500).Select(v => (double)v)
        from w in Gen.Choose(10, 500).Select(v => (double)v)
        from h in Gen.Choose(10, 500).Select(v => (double)v)
        select new Rect(x, y, w, h);

    private static Gen<int> FeatherRadiusGen =>
        Gen.Choose(0, 200);

    /// <summary>
    /// **Validates: Requirements 5.2**
    /// Runs on a single STA thread to avoid exhausting Win32 Dispatcher resources.
    /// </summary>
    [Fact]
    public void FeatherRadius_Controls_Gradient_Extent()
    {
        if (!StaHelper.CanRunWpf) return; // WPF rendering not available in this environment

        StaHelper.Run(() =>
        {
            var prop = Prop.ForAll(
                CutoutRectGen.ToArbitrary(),
                FeatherRadiusGen.ToArbitrary(),
                (cutout, featherRadius) =>
                {
                    _settings.FeatherRadius = featherRadius;

                    var renderer = new SpotlightRenderer(_settings);
                    renderer.AddCutout(cutout);

                    var overlaySize = new Size(3840, 2160);
                    var mask = renderer.BuildOpacityMask(overlaySize);

                    var cutoutDrawing = (GeometryDrawing)mask.Children[1];
                    var geometryBounds = cutoutDrawing.Geometry.Bounds;

                    double expectedWidth = cutout.Width + 2 * featherRadius;
                    double expectedHeight = cutout.Height + 2 * featherRadius;

                    const double tolerance = 0.001;

                    bool widthMatch = Math.Abs(geometryBounds.Width - expectedWidth) < tolerance;
                    bool heightMatch = Math.Abs(geometryBounds.Height - expectedHeight) < tolerance;
                    bool xMatch = Math.Abs(geometryBounds.X - (cutout.X - featherRadius)) < tolerance;
                    bool yMatch = Math.Abs(geometryBounds.Y - (cutout.Y - featherRadius)) < tolerance;

                    return widthMatch && heightMatch && xMatch && yMatch;
                });

            prop.QuickCheckThrowOnFailure();
        });
    }
}
