using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Tests;

// Feature: box-tool, Property 9: BoxColor validation normalizes to uppercase
// Validates: Requirements 9.5
//
// For any valid 6-char hex string (mixed case), SettingsService.Validate should return
// a BoxColor equal to the input converted to uppercase.
public class BoxColorValidationPropertyTests
{
    private static readonly char[] HexChars = "0123456789ABCDEFabcdef".ToCharArray();

    private static Gen<string> MixedCaseHexColorGen() =>
        Gen.ArrayOf(6, Gen.Elements(HexChars))
           .Select(chars => new string(chars));

    public static Arbitrary<string> Arb_MixedCaseHexColor() =>
        MixedCaseHexColorGen().ToArbitrary();

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(BoxColorValidationPropertyTests) })]
    public Property Validate_BoxColor_Normalizes_To_Uppercase(string hexColor)
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
            BoxColor: hexColor);

        var result = SettingsService.Validate(settings);

        return (result.BoxColor == hexColor.ToUpperInvariant())
            .ToProperty();
    }
}
