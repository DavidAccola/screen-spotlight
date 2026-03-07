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
        var input = new AppSettings(opacity, radius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(input);

        return (result.OverlayOpacity >= 0.0 && result.OverlayOpacity <= 1.0)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Validate_Always_Clamps_FeatherRadius_To_NonNegative(double opacity, int radius)
    {
        var input = new AppSettings(opacity, radius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(input);

        return (result.FeatherRadius >= 0)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Validate_Preserves_InRange_Values(double opacity, int radius)
    {
        // Constrain inputs to valid ranges
        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);
        var clampedRadius = Math.Max(radius, 0);

        // Skip if the generated values were out of range (we only test in-range preservation here)
        if (double.IsNaN(opacity) || double.IsInfinity(opacity))
            return true.ToProperty();

        var inRangeInput = new AppSettings(clampedOpacity, clampedRadius, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(inRangeInput);

        return (result.OverlayOpacity == inRangeInput.OverlayOpacity
             && result.FeatherRadius == inRangeInput.FeatherRadius)
            .ToProperty();
    }
}
