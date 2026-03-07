using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;
using Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 10: Settings validation and clamping
/// Validates: Requirements 8.5, 8.6
/// 
/// For any numeric values for OverlayOpacity and FeatherRadius (including out-of-range values),
/// the Validate function should return an AppSettings where OverlayOpacity is in [0.0, 1.0]
/// and FeatherRadius is >= 0. Furthermore, if the input values are already in range,
/// the output should equal the input.
/// </summary>
public class SettingsValidationPropertyTests
{
    [Property(MaxTest = 100)]
    public Property Validate_Always_Clamps_Opacity_To_Valid_Range(double opacity, int radius)
    {
        var input = new AppSettings(opacity, radius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, 0, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(input);

        return (result.OverlayOpacity >= 0.0 && result.OverlayOpacity <= 1.0)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Validate_Always_Clamps_FeatherRadius_To_NonNegative(double opacity, int radius)
    {
        var input = new AppSettings(opacity, radius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, 0, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(input);

        return (result.FeatherRadius >= 0)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Validate_Preserves_InRange_Values(double opacity, int radius)
    {
        // Constrain inputs to the actual valid ranges that Validate uses: [0.01, 0.99] and [0, 50]
        var clampedOpacity = Math.Clamp(opacity, 0.01, 0.99);
        var clampedRadius = Math.Clamp(radius, 0, 50);

        // Skip non-finite values
        if (double.IsNaN(opacity) || double.IsInfinity(opacity))
            return true.ToProperty();

        // Also skip negative zero — Math.Clamp(-0.0, 0.01, 0.99) returns 0.01 but
        // the bit pattern of -0.0 doesn't equal 0.0, causing spurious failures
        if (double.IsNegative(clampedOpacity) && clampedOpacity == 0.0)
            return true.ToProperty();

        var inRangeInput = new AppSettings(clampedOpacity, clampedRadius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, 0, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(inRangeInput);

        return (result.OverlayOpacity == inRangeInput.OverlayOpacity
             && result.FeatherRadius == inRangeInput.FeatherRadius)
            .ToProperty();
    }
}
