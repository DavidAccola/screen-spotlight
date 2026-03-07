using System.IO;
using FsCheck;
using FsCheck.Xunit;
using SpotlightOverlay.Input;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;
using Xunit;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Tests for mouse button support in GlobalInputHook:
/// - IsMouseButtonVk helper
/// - Mouse VK to WM message mapping
/// - ModifierKey.None handling in activation/toggle checks
/// - Settings validation for mouse button VKs
/// - Display name helpers
/// </summary>
public class MouseButtonHookTests
{
    // ── IsMouseButtonVk via reflection ──────────────────────────────

    private static bool CallIsMouseButtonVk(int vk)
    {
        var method = typeof(GlobalInputHook).GetMethod("IsMouseButtonVk",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, new object[] { vk })!;
    }

    [Theory]
    [InlineData(0x04, true)]  // VK_MBUTTON (middle)
    [InlineData(0x05, true)]  // VK_XBUTTON1
    [InlineData(0x06, true)]  // VK_XBUTTON2
    [InlineData(0x01, false)] // VK_LBUTTON
    [InlineData(0x02, false)] // VK_RBUTTON
    [InlineData(0x11, false)] // VK_CONTROL
    [InlineData(0x51, false)] // VK_Q
    [InlineData(0x00, false)] // no key
    [InlineData(0x20, false)] // VK_SPACE
    public void IsMouseButtonVk_Returns_Correct_Result(int vk, bool expected)
    {
        Assert.Equal(expected, CallIsMouseButtonVk(vk));
    }

    // ── MouseVkToDownMsg / MouseVkToUpMsg via reflection ───────────

    private static int CallMouseVkToDownMsg(int vk)
    {
        var method = typeof(GlobalInputHook).GetMethod("MouseVkToDownMsg",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (int)method!.Invoke(null, new object[] { vk })!;
    }

    private static int CallMouseVkToUpMsg(int vk)
    {
        var method = typeof(GlobalInputHook).GetMethod("MouseVkToUpMsg",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (int)method!.Invoke(null, new object[] { vk })!;
    }

    [Theory]
    [InlineData(0x04, 0x0207)] // middle -> WM_MBUTTONDOWN
    [InlineData(0x05, 0x020B)] // X1 -> WM_XBUTTONDOWN
    [InlineData(0x06, 0x020B)] // X2 -> WM_XBUTTONDOWN (same msg, differentiated by mouseData)
    [InlineData(0x11, 0)]      // VK_CONTROL -> not a mouse button
    [InlineData(0x00, 0)]      // no key
    public void MouseVkToDownMsg_Maps_Correctly(int vk, int expectedMsg)
    {
        Assert.Equal(expectedMsg, CallMouseVkToDownMsg(vk));
    }

    [Theory]
    [InlineData(0x04, 0x0208)] // middle -> WM_MBUTTONUP
    [InlineData(0x05, 0x020C)] // X1 -> WM_XBUTTONUP
    [InlineData(0x06, 0x020C)] // X2 -> WM_XBUTTONUP
    [InlineData(0x11, 0)]      // VK_CONTROL -> not a mouse button
    public void MouseVkToUpMsg_Maps_Correctly(int vk, int expectedMsg)
    {
        Assert.Equal(expectedMsg, CallMouseVkToUpMsg(vk));
    }

    // ── IsXButtonMatch via reflection ──────────────────────────────

    private static bool CallIsXButtonMatch(int vk, uint mouseData)
    {
        var method = typeof(GlobalInputHook).GetMethod("IsXButtonMatch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, new object[] { vk, mouseData })!;
    }

    [Theory]
    [InlineData(0x05, 0x00010000u, true)]  // X1 with XBUTTON1 in high word
    [InlineData(0x05, 0x00020000u, false)] // X1 with XBUTTON2 in high word — mismatch
    [InlineData(0x06, 0x00020000u, true)]  // X2 with XBUTTON2 in high word
    [InlineData(0x06, 0x00010000u, false)] // X2 with XBUTTON1 in high word — mismatch
    [InlineData(0x04, 0x00000000u, true)]  // middle click — always matches (no xButton check)
    [InlineData(0x04, 0x00010000u, true)]  // middle click — ignores mouseData
    public void IsXButtonMatch_Differentiates_X1_And_X2(int vk, uint mouseData, bool expected)
    {
        Assert.Equal(expected, CallIsXButtonMatch(vk, mouseData));
    }

    // ── ModifierKey.None handling ──────────────────────────────────

    [Fact]
    public void Hook_With_None_Modifier_And_MouseButton_Activation_Has_Correct_State()
    {
        using var hook = new GlobalInputHook();
        hook.ActivationModifier = ModifierKey.None;
        hook.ActivationKey = 0x05; // Mouse 4

        // IsActivationModifierVk should return false for all keyboard keys when modifier is None
        var isModVk = typeof(GlobalInputHook).GetMethod("IsActivationModifierVk",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.False((bool)isModVk!.Invoke(hook, new object[] { (uint)0x11 })!); // VK_CONTROL
        Assert.False((bool)isModVk!.Invoke(hook, new object[] { (uint)0x10 })!); // VK_SHIFT
        Assert.False((bool)isModVk!.Invoke(hook, new object[] { (uint)0x12 })!); // VK_MENU
        // Mouse button VKs should also not match (handled in mouse hook, not keyboard)
        Assert.False((bool)isModVk!.Invoke(hook, new object[] { (uint)0x05 })!);
    }

    [Fact]
    public void Hook_With_None_Modifier_IsActivationModifierHeld_Returns_True()
    {
        // ModifierKey.None means no modifier is required — always "held"
        using var hook = new GlobalInputHook();
        hook.ActivationModifier = ModifierKey.None;
        hook.ActivationKey = 0x05; // Mouse 4

        var isHeld = typeof(GlobalInputHook).GetMethod("IsActivationModifierHeld",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // With None modifier, modsHeld is always true.
        // ActivationKey is a mouse button, so the keyboard check is skipped.
        // Result: true (the mouse button press is checked via WM messages, not here)
        Assert.True((bool)isHeld!.Invoke(hook, null)!);
    }

    [Fact]
    public void Hook_With_None_Toggle_Modifier_IsToggleModifierHeld_Returns_True()
    {
        using var hook = new GlobalInputHook();
        hook.ToggleModifier = ModifierKey.None;
        hook.ToggleKey = 0x04; // Middle click

        var isHeld = typeof(GlobalInputHook).GetMethod("IsToggleModifierHeld",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.True((bool)isHeld!.Invoke(hook, null)!);
    }

    // ── Settings validation for mouse button VKs ───────────────────

    [Theory]
    [InlineData(0x04)] // middle
    [InlineData(0x05)] // X1
    [InlineData(0x06)] // X2
    public void Settings_Validate_Accepts_Mouse_Button_ActivationKey(int vk)
    {
        var input = new AppSettings(0.75, 8, PreviewStyle.Crosshair, DragStyle.ClickClick,
            false, ModifierKey.None, vk, ModifierKey.CtrlShift, 0x51);
        var result = SettingsService.Validate(input);

        Assert.Equal(vk, result.ActivationKey);
        Assert.Equal(ModifierKey.None, result.ActivationModifier);
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x05)]
    [InlineData(0x06)]
    public void Settings_Validate_Accepts_Mouse_Button_ToggleKey(int vk)
    {
        var input = new AppSettings(0.75, 8, PreviewStyle.Crosshair, DragStyle.ClickClick,
            false, ModifierKey.Ctrl, 0, ModifierKey.None, vk);
        var result = SettingsService.Validate(input);

        Assert.Equal(vk, result.ToggleKey);
        Assert.Equal(ModifierKey.None, result.ToggleModifier);
    }

    [Fact]
    public void Settings_Validate_Accepts_ModifierKey_None()
    {
        var input = new AppSettings(0.75, 8, PreviewStyle.Crosshair, DragStyle.ClickClick,
            false, ModifierKey.None, 0x05, ModifierKey.None, 0x06);
        var result = SettingsService.Validate(input);

        Assert.Equal(ModifierKey.None, result.ActivationModifier);
        Assert.Equal(ModifierKey.None, result.ToggleModifier);
    }

    // ── Settings round-trip with mouse buttons ─────────────────────

    [Theory]
    [InlineData(ModifierKey.None, 0x05, ModifierKey.None, 0x06)]
    [InlineData(ModifierKey.Ctrl, 0x04, ModifierKey.CtrlShift, 0x05)]
    [InlineData(ModifierKey.None, 0x04, ModifierKey.Alt, 0x51)]
    public void Settings_RoundTrip_Preserves_Mouse_Button_Config(
        ModifierKey actMod, int actKey, ModifierKey togMod, int togKey)
    {
        var original = new AppSettings(0.75, 8, PreviewStyle.Crosshair, DragStyle.ClickClick,
            false, actMod, actKey, togMod, togKey);

        var json = SettingsService.Serialize(original);
        var deserialized = SettingsService.Deserialize(json);

        Assert.Equal(original.ActivationModifier, deserialized.ActivationModifier);
        Assert.Equal(original.ActivationKey, deserialized.ActivationKey);
        Assert.Equal(original.ToggleModifier, deserialized.ToggleModifier);
        Assert.Equal(original.ToggleKey, deserialized.ToggleKey);
    }

    // ── Settings persistence with mouse buttons ────────────────────

    [Fact]
    public void SettingsService_Save_Load_Preserves_Mouse_Button_Config()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        try
        {
            var svc = new SettingsService(tempPath);
            svc.ActivationModifier = ModifierKey.None;
            svc.ActivationKey = 0x05; // Mouse 4
            svc.ToggleModifier = ModifierKey.None;
            svc.ToggleKey = 0x06; // Mouse 5
            svc.Save();

            var svc2 = new SettingsService(tempPath);
            svc2.Load();

            Assert.Equal(ModifierKey.None, svc2.ActivationModifier);
            Assert.Equal(0x05, svc2.ActivationKey);
            Assert.Equal(ModifierKey.None, svc2.ToggleModifier);
            Assert.Equal(0x06, svc2.ToggleKey);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // ── VkDisplayName for mouse buttons ────────────────────────────

    [Theory]
    [InlineData(0x04, "Middle Click")]
    [InlineData(0x05, "Mouse 4")]
    [InlineData(0x06, "Mouse 5")]
    public void VkDisplayName_Returns_Correct_Mouse_Button_Names(int vk, string expected)
    {
        Assert.Equal(expected, Windows.SettingsWindow.VkDisplayName(vk));
    }

    // ── Hook DragStyle and modifier sync ───────────────────────────

    [Theory]
    [InlineData(ModifierKey.None)]
    [InlineData(ModifierKey.Ctrl)]
    [InlineData(ModifierKey.Alt)]
    [InlineData(ModifierKey.Shift)]
    [InlineData(ModifierKey.CtrlShift)]
    [InlineData(ModifierKey.CtrlAlt)]
    public void Hook_Accepts_All_ModifierKey_Values(ModifierKey mod)
    {
        using var hook = new GlobalInputHook();
        hook.ActivationModifier = mod;
        Assert.Equal(mod, hook.ActivationModifier);

        hook.ToggleModifier = mod;
        Assert.Equal(mod, hook.ToggleModifier);
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x05)]
    [InlineData(0x06)]
    [InlineData(0x51)] // VK_Q — keyboard key
    [InlineData(0x20)] // VK_SPACE
    [InlineData(0)]    // no key
    public void Hook_Accepts_All_ActivationKey_Values(int key)
    {
        using var hook = new GlobalInputHook();
        hook.ActivationKey = key;
        Assert.Equal(key, hook.ActivationKey);

        hook.ToggleKey = key == 0 ? 0x51 : key; // ToggleKey must be non-zero for valid config
        Assert.Equal(key == 0 ? 0x51 : key, hook.ToggleKey);
    }

    // ── IsRecordingHotkey suppression ──────────────────────────────

    [Fact]
    public void IsRecordingHotkey_Can_Be_Set_And_Read()
    {
        using var hook = new GlobalInputHook();
        Assert.False(hook.IsRecordingHotkey);

        hook.IsRecordingHotkey = true;
        Assert.True(hook.IsRecordingHotkey);

        hook.IsRecordingHotkey = false;
        Assert.False(hook.IsRecordingHotkey);
    }

    // ── Property: mouse VK mapping is consistent ───────────────────

    [Property(MaxTest = 50)]
    public Property MouseVk_DownUp_Messages_Are_Consistent()
    {
        var mouseVkGen = Gen.Elements(0x04, 0x05, 0x06).ToArbitrary();

        return Prop.ForAll(mouseVkGen, vk =>
        {
            int down = CallMouseVkToDownMsg(vk);
            int up = CallMouseVkToUpMsg(vk);

            // Down and up messages should both be non-zero for valid mouse VKs
            bool bothNonZero = down != 0 && up != 0;
            // Up message should be exactly 1 more than down message for each button type
            bool upIsDownPlusOne = up == down + 1;

            return bothNonZero && upIsDownPlusOne;
        });
    }

    // ── Property: non-mouse VKs always map to 0 ───────────────────

    [Property(MaxTest = 100)]
    public Property NonMouse_Vk_Maps_To_Zero_Messages(int vk)
    {
        // Skip actual mouse button VKs
        if (vk is 0x04 or 0x05 or 0x06)
            return true.ToProperty();

        int down = CallMouseVkToDownMsg(vk);
        int up = CallMouseVkToUpMsg(vk);

        return (down == 0 && up == 0).ToProperty();
    }

    // ── Property: IsXButtonMatch is symmetric for X1/X2 ───────────

    [Fact]
    public void XButton_Match_Is_Exclusive_Between_X1_And_X2()
    {
        uint x1Data = 0x00010000u; // XBUTTON1 in high word
        uint x2Data = 0x00020000u; // XBUTTON2 in high word

        // X1 VK matches X1 data, not X2 data
        Assert.True(CallIsXButtonMatch(0x05, x1Data));
        Assert.False(CallIsXButtonMatch(0x05, x2Data));

        // X2 VK matches X2 data, not X1 data
        Assert.True(CallIsXButtonMatch(0x06, x2Data));
        Assert.False(CallIsXButtonMatch(0x06, x1Data));
    }

    // ── IsActivationModifierFullyReleased with None ────────────────

    [Fact]
    public void IsActivationModifierFullyReleased_Returns_True_For_None()
    {
        using var hook = new GlobalInputHook();
        hook.ActivationModifier = ModifierKey.None;
        hook.ActivationKey = 0x05;

        var method = typeof(GlobalInputHook).GetMethod("IsActivationModifierFullyReleased",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // None modifier is always "released" — mouse button up handles apply
        Assert.True((bool)method!.Invoke(hook, null)!);
    }
}
