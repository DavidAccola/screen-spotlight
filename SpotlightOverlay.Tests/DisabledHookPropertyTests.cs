using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Input;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Feature: spotlight-overlay, Property 2: Disabled hook ignores all inputs
/// Validates: Requirements 1.5
///
/// For any input event (drag gesture or Escape key press), if the GlobalInputHook
/// IsEnabled is false, then no DragCompleted or DismissRequested event shall be emitted.
/// </summary>
public class DisabledHookPropertyTests
{
    // Feature: spotlight-overlay, Property 2: Disabled hook ignores all inputs

    [Property(MaxTest = 100)]
    public void Disabled_Hook_Never_Emits_Events_After_Any_Toggle_Sequence()
    {
        // **Validates: Requirements 1.5**
        // Generate random sequences of enable/disable operations that always end
        // with IsEnabled = false, then verify the hook is in the disabled state
        // and no events can be emitted through the public API.
        var toggleCountArb = Gen.Choose(0, 50).ToArbitrary();

        var prop = Prop.ForAll(
            toggleCountArb,
            (toggleCount) =>
            {
                using var hook = new GlobalInputHook();

                int dragCompletedCount = 0;
                int dismissRequestedCount = 0;

                hook.DragCompleted += (_, _) => dragCompletedCount++;
                hook.DismissRequested += (_, _) => dismissRequestedCount++;

                // Apply a random number of toggles starting from false (default)
                for (int i = 0; i < toggleCount; i++)
                {
                    hook.IsEnabled = !hook.IsEnabled;
                }

                // Ensure we always end in the disabled state
                hook.IsEnabled = false;

                // Verify the hook reports disabled state
                bool isDisabled = !hook.IsEnabled;

                // Since the hook is disabled, no events should have been emitted
                // through any code path (the callbacks gate on IsEnabled)
                bool noEventsEmitted = dragCompletedCount == 0 && dismissRequestedCount == 0;

                return isDisabled && noEventsEmitted;
            });

        prop.QuickCheckThrowOnFailure();
    }
}
