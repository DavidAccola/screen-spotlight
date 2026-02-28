using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Input;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 1: Toggle hook state is consistent
/// Validates: Requirements 1.4
///
/// For any number of toggles N applied to the GlobalInputHook starting from
/// an initial enabled/disabled state, the resulting IsEnabled state should
/// equal the initial state XOR (N is odd).
/// </summary>
public class ToggleHookStatePropertyTests
{
    // Feature: spotlight-overlay, Property 1: Toggle hook state is consistent

    [Property(MaxTest = 100)]
    public void Toggle_State_Is_Consistent_With_XOR_Parity()
    {
        // **Validates: Requirements 1.4**
        var prop = Prop.ForAll(
            Arb.From<bool>(),
            Gen.Choose(0, 200).ToArbitrary(),
            (initialState, toggleCount) =>
            {
                using var hook = new GlobalInputHook();
                hook.IsEnabled = initialState;

                for (int i = 0; i < toggleCount; i++)
                {
                    hook.IsEnabled = !hook.IsEnabled;
                }

                bool expected = initialState ^ (toggleCount % 2 == 1);
                return hook.IsEnabled == expected;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
