using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: flyout-toolbar, Property 3: Settings serialization round-trip
/// Validates: Requirements 7.1
///
/// For any valid AppSettings instance (including ToolbarAnchorEdge and FlyoutToolbarVisible),
/// serializing via SettingsService.Serialize and then deserializing via SettingsService.Deserialize
/// shall produce an AppSettings record equal to the original.
/// </summary>
public class ToolbarSettingsPropertyTests
{
    private static Gen<PreviewStyle> PreviewStyleGen =>
        Gen.Elements(PreviewStyle.Outline, PreviewStyle.Crosshair, PreviewStyle.Corners);

    private static Gen<DragStyle> DragStyleGen =>
        Gen.Elements(DragStyle.HoldDrag, DragStyle.ClickClick);

    private static Gen<ModifierKey> ModifierKeyGen =>
        Gen.Elements(ModifierKey.Ctrl, ModifierKey.Alt, ModifierKey.Shift,
                      ModifierKey.CtrlShift, ModifierKey.CtrlAlt, ModifierKey.None);

    private static Gen<AnchorEdge> AnchorEdgeGen =>
        Gen.Elements(AnchorEdge.Left, AnchorEdge.Right, AnchorEdge.Top);

    private static Gen<AppSettings> AppSettingsGen =>
        from opacity in Gen.Choose(1, 99).Select(v => v / 100.0)
        from feather in Gen.Choose(0, 50)
        from preview in PreviewStyleGen
        from drag in DragStyleGen
        from freeze in Gen.Elements(true, false)
        from actMod in ModifierKeyGen
        from actKey in Gen.Choose(0x00, 0xFE)
        from togMod in ModifierKeyGen
        from togKey in Gen.Choose(0x01, 0xFE)
        from cumulative in Gen.Elements(true, false)
        from anchor in AnchorEdgeGen
        from flyoutVisible in Gen.Elements(true, false)
        select new AppSettings(
            opacity, feather, preview, drag, freeze,
            actMod, actKey, togMod, togKey,
            cumulative, anchor, flyoutVisible);

    // Feature: flyout-toolbar, Property 3: Settings serialization round-trip
    /// <summary>
    /// **Validates: Requirements 7.1**
    /// For any valid AppSettings instance, Deserialize(Serialize(settings)) == settings.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Serialize_Then_Deserialize_Returns_Equal_Settings()
    {
        var prop = Prop.ForAll(
            AppSettingsGen.ToArbitrary(),
            settings =>
            {
                var json = SettingsService.Serialize(settings);
                var roundTripped = SettingsService.Deserialize(json);
                return roundTripped == settings;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // Feature: flyout-toolbar, Property 6: Settings validation clamps anchor edge
    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    /// For any integer value cast to AnchorEdge (including out-of-range values),
    /// SettingsService.Validate shall return an AppSettings with a valid AnchorEdge
    /// enum value (Left, Right, or Top).
    /// </summary>
    [Property(MaxTest = 100)]
    public void Validate_Always_Returns_Defined_AnchorEdge()
    {
        var arbitraryInt = Gen.Choose(-100, 100).ToArbitrary();

        var prop = Prop.ForAll(
            arbitraryInt,
            rawValue =>
            {
                var anchorEdge = (AnchorEdge)rawValue;
                var settings = new AppSettings(
                    0.75, 8, PreviewStyle.Crosshair, DragStyle.ClickClick, true,
                    ModifierKey.Ctrl, 0, ModifierKey.CtrlShift, 0x51,
                    true, anchorEdge, true);

                var result = SettingsService.Validate(settings);

                return Enum.IsDefined(typeof(AnchorEdge), result.ToolbarAnchorEdge);
            });

        prop.QuickCheckThrowOnFailure();
    }
}
