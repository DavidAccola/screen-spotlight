using System.IO;
using System.Windows;
using System.Windows.Media;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Rendering;
using SpotlightOverlay.Services;
using Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 6: Opacity mask reflects all active cutouts
/// Validates: Requirements 4.3, 5.3
///
/// For any set of cutout rectangles added to the SpotlightRenderer, the DrawingGroup
/// returned by BuildOpacityMask should contain exactly one drawing element per cutout
/// plus one background element, all combined in a single DrawingGroup.
/// </summary>
public class OpacityMaskPropertyTests : IDisposable
{
    private readonly string _tempFilePath;
    private readonly SettingsService _settings;

    public OpacityMaskPropertyTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        _settings = new SettingsService(_tempFilePath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
            File.Delete(_tempFilePath);
    }

    private static Gen<Rect> RectGen =>
        from x in Gen.Choose(0, 2000).Select(v => (double)v)
        from y in Gen.Choose(0, 2000).Select(v => (double)v)
        from w in Gen.Choose(1, 500).Select(v => (double)v)
        from h in Gen.Choose(1, 500).Select(v => (double)v)
        select new Rect(x, y, w, h);

    /// <summary>
    /// **Validates: Requirements 4.3, 5.3**
    /// The DrawingGroup children count should equal 1 (background) + N (cutouts).
    ///
    /// Runs on a single STA thread to avoid exhausting Win32 Dispatcher resources.
    /// </summary>
    [Fact]
    public void OpacityMask_Contains_Background_Plus_One_Drawing_Per_Cutout()
    {
        if (!StaHelper.CanRunWpf) return; // WPF rendering not available in this environment

        StaHelper.Run(() =>
        {
            var prop = Prop.ForAll(RectGen.ListOf().ToArbitrary(), rects =>
            {
                var renderer = new SpotlightRenderer(_settings);
                var overlaySize = new System.Windows.Size(3840, 2160);

                foreach (var rect in rects)
                    renderer.AddCutout(rect);

                var mask = renderer.BuildOpacityMask(overlaySize);

                int expectedCount = 1 + rects.Count;
                return mask.Children.Count == expectedCount;
            });

            prop.QuickCheckThrowOnFailure();
        });
    }
}
