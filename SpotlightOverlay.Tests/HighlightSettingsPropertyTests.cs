using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: highlight-tool
///
/// Property 7: Settings serialization round-trip for HighlightColor and HighlightOpacity
/// Validates: Requirements 9.2, 9.3, 10.2, 10.3
///
/// Property 8: HighlightColor validation normalizes to uppercase
/// Validates: Requirements 9.5
///
/// Property 9: HighlightColor invalid input falls back to default "FFC90E"
/// Validates: Requirements 9.4
///
/// Property 10: HighlightOpacity clamping to [0.1, 0.9]
/// Validates: Requirements 10.4, 10.5
/// </summary>
public class HighlightSettingsPropertyTests
{
    private static readonly char[] HexChars = "0123456789ABCDEFabcdef".ToCharArray();

    private static Gen<string> ValidHexColorGen() =>
        Gen.ArrayOf(6, Gen.Elements(HexChars))
           .Select(chars => new string(chars));

    private static Gen<double> ValidOpacityGen() =>
        Gen.Choose(0, 1_000_000)
           .Select(i => 0.1 + (i / 1_000_000.0) * 0.8); // maps to [0.1, 0.9]

    private static AppSettings MakeSettings(string color, double opacity) =>
        new AppSettings(
            OverlayOpacity: 0.75,
            FeatherRadius: 8,
            PreviewStyle: PreviewStyle.Crosshair,
            DragStyle: DragStyle.ClickClick,
            FreezeScreen: false,
            ActivationModifier: ModifierKey.Ctrl,
            ActivationKey: 0,
            ToggleModifier: ModifierKey.CtrlShift,
            ToggleKey: 0x51,
            HighlightColor: color,
            HighlightOpacity: opacity);

    // ── Property 7: Round-trip ─────────────────────────────────────

    public static Arbitrary<(string, double)> Arb_HighlightSettings()
    {
        var gen = from color in ValidHexColorGen()
                  from opacity in ValidOpacityGen()
                  select (color, opacity);
        return gen.ToArbitrary();
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(HighlightSettingsPropertyTests) })]
    public Property HighlightColor_And_Opacity_Survive_Serialize_Deserialize_RoundTrip(
        (string color, double opacity) input)
    {
        var settings = MakeSettings(input.color, input.opacity);
        var validated = SettingsService.Validate(settings);
        var json = SettingsService.Serialize(validated);
        var deserialized = SettingsService.Deserialize(json);

        return (deserialized.HighlightColor == validated.HighlightColor
             && deserialized.HighlightOpacity == validated.HighlightOpacity)
            .ToProperty();
    }

    // ── Property 8: Uppercase normalization ───────────────────────

    public static Arbitrary<string> Arb_MixedCaseHexColor() =>
        ValidHexColorGen().ToArbitrary();

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(HighlightSettingsPropertyTests) })]
    public Property Validate_HighlightColor_Normalizes_To_Uppercase(string hexColor)
    {
        var settings = MakeSettings(hexColor, 0.5);
        var result = SettingsService.Validate(settings);
        return (result.HighlightColor == hexColor.ToUpperInvariant()).ToProperty();
    }

    // ── Property 9: Invalid color falls back to default ───────────

    private static Gen<string> InvalidHexColorGen() =>
        Gen.OneOf(
            // Wrong length (not 6 chars)
            Gen.Choose(0, 20)
               .Where(n => n != 6)
               .SelectMany(n => Gen.ArrayOf(n, Gen.Elements(HexChars)))
               .Select(chars => new string(chars)),
            // 6 chars but contains at least one non-hex char ('G'-'Z' or 'g'-'z')
            // by replacing the first char with a guaranteed non-hex char
            Gen.ArrayOf(5, Gen.Elements(HexChars))
               .SelectMany(rest =>
                   Gen.Elements('G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P',
                                'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
                                'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p',
                                'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z')
                   .Select(nonHex => nonHex + new string(rest)))
        );

    public static Arbitrary<string> Arb_InvalidHexColor() =>
        Arb.From(InvalidHexColorGen(), s =>
            // Only shrink to shorter strings (which are still invalid due to wrong length)
            s.Length > 0
                ? new[] { s.Substring(0, Math.Max(0, s.Length - 1)) }.Where(t => t.Length != 6).AsEnumerable()
                : Enumerable.Empty<string>());

    [Property(MaxTest = 200)]
    public Property Validate_InvalidHighlightColor_FallsBackToDefault()
    {
        return Prop.ForAll(
            InvalidHexColorGen().ToArbitrary(),
            invalidColor =>
            {
                var settings = MakeSettings(invalidColor, 0.5);
                var result = SettingsService.Validate(settings);
                return (result.HighlightColor == "FFC90E").ToProperty();
            });
    }

    // ── Property 10: Opacity clamping ─────────────────────────────

    [Property(MaxTest = 200)]
    public Property Validate_HighlightOpacity_ClampedToRange(double rawOpacity)
    {
        var settings = MakeSettings("FFC90E", rawOpacity);
        var result = SettingsService.Validate(settings);

        if (double.IsNaN(rawOpacity) || double.IsInfinity(rawOpacity))
            return (result.HighlightOpacity == 0.5).ToProperty();

        return (result.HighlightOpacity >= 0.1 && result.HighlightOpacity <= 0.9).ToProperty();
    }
}
