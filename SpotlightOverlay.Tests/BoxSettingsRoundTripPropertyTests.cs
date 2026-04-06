using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Tests;

// Feature: box-tool, Property 8: Settings serialization round-trip
// Validates: Requirements 9.2, 9.3, 10.2, 10.3
//
// For any valid hex color string (6 chars from [0-9A-Fa-f]) and thickness in [1.0, 12.0],
// serializing to JSON and deserializing should preserve BoxColor and BoxLineThickness exactly.
public class BoxSettingsRoundTripPropertyTests
{
    private static readonly char[] HexChars = "0123456789ABCDEFabcdef".ToCharArray();

    private static Gen<string> HexColorGen() =>
        Gen.ArrayOf(6, Gen.Elements(HexChars))
           .Select(chars => new string(chars));

    private static Gen<double> ThicknessGen() =>
        Gen.Choose(0, 1_000_000)
           .Select(i => 1.0 + (i / 1_000_000.0) * 11.0); // maps to [1.0, 12.0]

    private static Arbitrary<(string color, double thickness)> BoxSettingsArbitrary()
    {
        var gen = from color in HexColorGen()
                  from thickness in ThicknessGen()
                  select (color, thickness);
        return gen.ToArbitrary();
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(BoxSettingsRoundTripPropertyTests) })]
    public Property BoxColor_And_BoxLineThickness_Survive_Serialize_Deserialize_RoundTrip(
        (string color, double thickness) input)
    {
        var settings = new AppSettings(
            OverlayOpacity: 0.75,
            FeatherRadius: 8,
            PreviewStyle: PreviewStyle.Crosshair,
            DragStyle: DragStyle.ClickClick,
            FreezeScreen: false,
            ActivationModifier: ModifierKey.Ctrl,
            ActivationKey: 0,
            ToggleModifier: ModifierKey.CtrlShift,
            ToggleKey: 0x51,
            BoxColor: input.color,
            BoxLineThickness: input.thickness);

        var validated = SettingsService.Validate(settings);
        var json = SettingsService.Serialize(validated);
        var deserialized = SettingsService.Deserialize(json);

        return (deserialized.BoxColor == validated.BoxColor
             && deserialized.BoxLineThickness == validated.BoxLineThickness)
            .ToProperty();
    }

    public static Arbitrary<(string, double)> Arb_BoxSettings() => BoxSettingsArbitrary();
}
