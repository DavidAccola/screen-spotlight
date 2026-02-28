using System.IO;
using System.Windows;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Rendering;
using SpotlightOverlay.Services;
using Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 5: Cutout accumulation preserves all entries
/// Validates: Requirements 4.1
///
/// For any sequence of N distinct rectangles added to the SpotlightRenderer,
/// the Cutouts list should contain exactly those N rectangles in insertion order,
/// and CutoutCount should equal N.
/// </summary>
public class CutoutAccumulationPropertyTests : IDisposable
{
    private readonly string _tempFilePath;
    private readonly SettingsService _settings;

    public CutoutAccumulationPropertyTests()
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

    [Property(MaxTest = 100)]
    public void CutoutCount_Equals_Number_Of_Added_Rectangles()
    {
        var prop = Prop.ForAll(RectGen.ListOf().ToArbitrary(), rects =>
        {
            var renderer = new SpotlightRenderer(_settings);

            foreach (var rect in rects)
                renderer.AddCutout(rect);

            return renderer.CutoutCount == rects.Count;
        });

        prop.QuickCheckThrowOnFailure();
    }

    [Property(MaxTest = 100)]
    public void Cutouts_Preserves_All_Entries_In_Insertion_Order()
    {
        var prop = Prop.ForAll(RectGen.ListOf().ToArbitrary(), rects =>
        {
            var renderer = new SpotlightRenderer(_settings);

            foreach (var rect in rects)
                renderer.AddCutout(rect);

            var cutouts = renderer.Cutouts;

            if (cutouts.Count != rects.Count)
                return false;

            for (int i = 0; i < rects.Count; i++)
            {
                if (cutouts[i] != rects[i])
                    return false;
            }

            return true;
        });

        prop.QuickCheckThrowOnFailure();
    }
}
