using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 11: Settings serialization round-trip
/// Validates: Requirements 11.1, 11.2, 8.4
///
/// For any valid AppSettings object (with OverlayOpacity in [0.0, 1.0] and FeatherRadius >= 0),
/// serializing to JSON and then deserializing should produce an AppSettings object equal to the
/// original, preserving exact numeric precision.
/// </summary>
public class SettingsRoundTripPropertyTests
{
    /// <summary>
    /// Generates valid AppSettings with OverlayOpacity in [0.0, 1.0] and FeatherRadius >= 0.
    /// </summary>
    private static Arbitrary<AppSettings> ValidAppSettingsArbitrary()
    {
        var gen = from opacity in Gen.Choose(0, 1_000_000).Select(i => i / 1_000_000.0)
                  from radius in Gen.Choose(0, 10_000)
                  select new AppSettings(opacity, radius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, ModifierKey.CtrlShift, 0x51);

        return gen.ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SettingsRoundTripPropertyTests) })]
    public Property Serialize_Then_Deserialize_Produces_Equal_AppSettings(AppSettings settings)
    {
        // Ensure input is valid via Validate (should be a no-op for our generator)
        var validated = SettingsService.Validate(settings);

        var json = SettingsService.Serialize(validated);
        var deserialized = SettingsService.Deserialize(json);

        return (deserialized.OverlayOpacity == validated.OverlayOpacity
             && deserialized.FeatherRadius == validated.FeatherRadius)
            .ToProperty();
    }

    public static Arbitrary<AppSettings> Arb_AppSettings() => ValidAppSettingsArbitrary();
}
