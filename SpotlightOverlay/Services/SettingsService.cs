using System.IO;
using System.Text.Json;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Services;

public class SettingsService
{
    private const double DefaultOverlayOpacity = 0.75;
    private const int DefaultFeatherRadius = 8;
    private const PreviewStyle DefaultPreviewStyle = PreviewStyle.Crosshair;
    private const DragStyle DefaultDragStyle = DragStyle.ClickClick;
    private const bool DefaultFreezeScreen = true;
    private const ModifierKey DefaultActivationModifier = ModifierKey.CtrlShift;
    private const int DefaultActivationKey = 0; // 0 = no key, modifier-only
    private const ModifierKey DefaultToggleModifier = ModifierKey.CtrlShift;
    private const int DefaultToggleKey = 0x51; // VK_Q
    private const ModifierKey DefaultToggleToolModifier = ModifierKey.CtrlShift;
    private const int DefaultToggleToolKey = 0x02; // VK_RBUTTON (right click = non-dominant by default)
    private const FadeMode DefaultFadeMode = FadeMode.Immediately;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_SWAPBUTTON = 23;

    /// <summary>Returns the non-dominant mouse button VK: right-click (0x02) for right-handed, left-click (0x01) for left-handed.</summary>
    private static int GetNonDominantMouseButtonVk() =>
        GetSystemMetrics(SM_SWAPBUTTON) != 0 ? 0x01 : 0x02;
    private const bool DefaultCumulativeSpotlights = true;
    private const AnchorEdge DefaultToolbarAnchorEdge = AnchorEdge.Right;
    private const bool DefaultFlyoutToolbarVisible = true;
    private const ArrowheadStyle DefaultArrowheadStyle = ArrowheadStyle.None;
    private const ArrowheadStyle DefaultArrowEndStyle = ArrowheadStyle.FilledTriangle;
    private const ArrowLineStyle DefaultArrowLineStyle = ArrowLineStyle.Solid;
    private const string DefaultArrowColor = "FF0000";
    private const double DefaultArrowLeftEndSize = 16.0;
    private const double DefaultArrowLineThickness = 3.0;
    private const double DefaultArrowRightEndSize = 16.0;
    private const bool DefaultSyncArrowEndStyle = true;
    private const bool DefaultSyncArrowEndSize = true;
    private const string DefaultCustomColors = "";
    private const string SettingsFileName = "Settings.json";

    private readonly string _settingsFilePath;

    public double OverlayOpacity { get; set; } = DefaultOverlayOpacity;
    public int FeatherRadius { get; set; } = DefaultFeatherRadius;
    public PreviewStyle PreviewStyle { get; set; } = DefaultPreviewStyle;
    public DragStyle DragStyle { get; set; } = DefaultDragStyle;
    public bool FreezeScreen { get; set; } = DefaultFreezeScreen;
    public ModifierKey ActivationModifier { get; set; } = DefaultActivationModifier;
    public int ActivationKey { get; set; } = DefaultActivationKey;
    public ModifierKey ToggleModifier { get; set; } = DefaultToggleModifier;
    public int ToggleKey { get; set; } = DefaultToggleKey;
    public ModifierKey ToggleToolModifier { get; set; } = DefaultToggleToolModifier;
    public int ToggleToolKey { get; set; } = DefaultToggleToolKey;
    public FadeMode FadeMode { get; set; } = DefaultFadeMode;
    public bool CumulativeSpotlights { get; set; } = DefaultCumulativeSpotlights;    public AnchorEdge ToolbarAnchorEdge { get; set; } = DefaultToolbarAnchorEdge;
    public bool FlyoutToolbarVisible { get; set; } = DefaultFlyoutToolbarVisible;
    public ArrowheadStyle ArrowheadStyle { get; set; } = DefaultArrowheadStyle;
    public ArrowheadStyle ArrowEndStyle { get; set; } = DefaultArrowEndStyle;
    public ArrowLineStyle ArrowLineStyle { get; set; } = DefaultArrowLineStyle;
    public string ArrowColor { get; set; } = DefaultArrowColor;
    public double ArrowLeftEndSize { get; set; } = DefaultArrowLeftEndSize;
    public double ArrowLineThickness { get; set; } = DefaultArrowLineThickness;
    public double ArrowRightEndSize { get; set; } = DefaultArrowRightEndSize;
    public bool SyncArrowEndStyle { get; set; } = DefaultSyncArrowEndStyle;
    public bool SyncArrowEndSize { get; set; } = DefaultSyncArrowEndSize;
    public string CustomColors { get; set; } = DefaultCustomColors;

    /// <summary>Fired after Save() so listeners can react to any setting change.</summary>
    public event EventHandler? SettingsChanged;

    public SettingsService()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName)) { }

    public SettingsService(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public void ResetToDefaults()
    {
        SetDefaults();
        Save();
    }

    private void SetDefaults()
    {
        OverlayOpacity = DefaultOverlayOpacity;
        FeatherRadius = DefaultFeatherRadius;
        PreviewStyle = DefaultPreviewStyle;
        DragStyle = DefaultDragStyle;
        FreezeScreen = DefaultFreezeScreen;
        ActivationModifier = DefaultActivationModifier;
        ActivationKey = DefaultActivationKey;
        ToggleModifier = DefaultToggleModifier;
        ToggleKey = DefaultToggleKey;
        ToggleToolModifier = DefaultToggleToolModifier;
        ToggleToolKey = GetNonDominantMouseButtonVk();
        FadeMode = DefaultFadeMode;
        CumulativeSpotlights = DefaultCumulativeSpotlights;
        ToolbarAnchorEdge = DefaultToolbarAnchorEdge;
        FlyoutToolbarVisible = DefaultFlyoutToolbarVisible;
        ArrowheadStyle = DefaultArrowheadStyle;
        ArrowEndStyle = DefaultArrowEndStyle;
        ArrowLineStyle = DefaultArrowLineStyle;
        ArrowColor = DefaultArrowColor;
        ArrowLeftEndSize = DefaultArrowLeftEndSize;
        ArrowLineThickness = DefaultArrowLineThickness;
        ArrowRightEndSize = DefaultArrowRightEndSize;
        SyncArrowEndStyle = DefaultSyncArrowEndStyle;
        SyncArrowEndSize = DefaultSyncArrowEndSize;
        CustomColors = DefaultCustomColors;
    }

    public void Load()
    {
        if (!File.Exists(_settingsFilePath)) { SetDefaults(); Save(); return; }

        string json;
        try { json = File.ReadAllText(_settingsFilePath); }
        catch { SetDefaults(); return; }

        AppSettings settings;
        try { settings = Deserialize(json); }
        catch { SetDefaults(); return; }

        var v = Validate(settings);
        OverlayOpacity = v.OverlayOpacity;
        FeatherRadius = v.FeatherRadius;
        PreviewStyle = v.PreviewStyle;
        DragStyle = v.DragStyle;
        FreezeScreen = v.FreezeScreen;
        ActivationModifier = v.ActivationModifier;
        ActivationKey = v.ActivationKey;
        ToggleModifier = v.ToggleModifier;
        ToggleKey = v.ToggleKey;
        ToggleToolModifier = v.ToggleToolModifier;
        ToggleToolKey = v.ToggleToolKey;
        FadeMode = v.FadeMode;
        CumulativeSpotlights = v.CumulativeSpotlights;
        ToolbarAnchorEdge = v.ToolbarAnchorEdge;
        FlyoutToolbarVisible = v.FlyoutToolbarVisible;
        ArrowheadStyle = v.ArrowheadStyle;
        ArrowEndStyle = v.ArrowEndStyle;
        ArrowLineStyle = v.ArrowLineStyle;
        ArrowColor = v.ArrowColor;
        ArrowLeftEndSize = v.ArrowLeftEndSize;
        ArrowLineThickness = v.ArrowLineThickness;
        ArrowRightEndSize = v.ArrowRightEndSize;
        SyncArrowEndStyle = v.SyncArrowEndStyle;
        SyncArrowEndSize = v.SyncArrowEndSize;
        CustomColors = v.CustomColors;
    }

    public void Save()
    {
        var json = Serialize(new AppSettings(OverlayOpacity, FeatherRadius, PreviewStyle, DragStyle, FreezeScreen, ActivationModifier, ActivationKey, ToggleModifier, ToggleKey, CumulativeSpotlights, ToolbarAnchorEdge, FlyoutToolbarVisible, ArrowheadStyle, ArrowEndStyle, ArrowLineStyle, ArrowColor, ArrowLeftEndSize, ArrowLineThickness, ArrowRightEndSize, SyncArrowEndStyle, SyncArrowEndSize, CustomColors, ToggleToolModifier, ToggleToolKey, FadeMode));
        File.WriteAllText(_settingsFilePath, json);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json)
        ?? new AppSettings(DefaultOverlayOpacity, DefaultFeatherRadius, DefaultPreviewStyle, DefaultDragStyle, DefaultFreezeScreen, DefaultActivationModifier, DefaultActivationKey, DefaultToggleModifier, DefaultToggleKey, DefaultCumulativeSpotlights, DefaultToolbarAnchorEdge, DefaultFlyoutToolbarVisible, DefaultArrowheadStyle, DefaultArrowEndStyle, DefaultArrowLineStyle, DefaultArrowColor, DefaultArrowLeftEndSize, DefaultArrowLineThickness, DefaultArrowRightEndSize, DefaultSyncArrowEndStyle, DefaultSyncArrowEndSize, DefaultCustomColors, DefaultToggleToolModifier, DefaultToggleToolKey, DefaultFadeMode);

    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings);

    public static AppSettings Validate(AppSettings s)
    {
        var opacity = double.IsNaN(s.OverlayOpacity) || double.IsInfinity(s.OverlayOpacity)
            ? DefaultOverlayOpacity : Math.Clamp(s.OverlayOpacity, 0.01, 0.99);
        var radius = Math.Clamp(s.FeatherRadius, 0, 50);
        var preview = Enum.IsDefined(s.PreviewStyle) ? s.PreviewStyle : DefaultPreviewStyle;
        var drag = Enum.IsDefined(s.DragStyle) ? s.DragStyle : DefaultDragStyle;
        var modifier = Enum.IsDefined(s.ActivationModifier) ? s.ActivationModifier : DefaultActivationModifier;
        var activationKey = s.ActivationKey is >= 0x00 and <= 0xFE ? s.ActivationKey : DefaultActivationKey;
        var toggleMod = Enum.IsDefined(s.ToggleModifier) ? s.ToggleModifier : DefaultToggleModifier;
        var toggleKey = s.ToggleKey is >= 0x01 and <= 0xFE ? s.ToggleKey : DefaultToggleKey;
        var anchorEdge = Enum.IsDefined(s.ToolbarAnchorEdge) ? s.ToolbarAnchorEdge : DefaultToolbarAnchorEdge;
        var arrowheadStyle = Enum.IsDefined(s.ArrowheadStyle) ? s.ArrowheadStyle : DefaultArrowheadStyle;
        var arrowEndStyle = Enum.IsDefined(s.ArrowEndStyle) ? s.ArrowEndStyle : DefaultArrowEndStyle;
        var arrowLineStyle = Enum.IsDefined(s.ArrowLineStyle) ? s.ArrowLineStyle : DefaultArrowLineStyle;
        var arrowColor = IsValidHexColor(s.ArrowColor) ? s.ArrowColor.ToUpperInvariant() : DefaultArrowColor;
        var leftSize = Math.Clamp(s.ArrowLeftEndSize, 8, 60);
        var lineThick = Math.Clamp(s.ArrowLineThickness, 1, 12);
        var rightSize = Math.Clamp(s.ArrowRightEndSize, 8, 60);
        var toggleToolMod = Enum.IsDefined(s.ToggleToolModifier) ? s.ToggleToolModifier : DefaultToggleToolModifier;
        var toggleToolKey = s.ToggleToolKey is >= 0x01 and <= 0xFE ? s.ToggleToolKey : DefaultToggleToolKey;
        var fadeMode = Enum.IsDefined(s.FadeMode) ? s.FadeMode : DefaultFadeMode;
        return new AppSettings(opacity, radius, preview, drag, s.FreezeScreen, modifier, activationKey, toggleMod, toggleKey, s.CumulativeSpotlights, anchorEdge, s.FlyoutToolbarVisible, arrowheadStyle, arrowEndStyle, arrowLineStyle, arrowColor, leftSize, lineThick, rightSize, s.SyncArrowEndStyle, s.SyncArrowEndSize, s.CustomColors ?? DefaultCustomColors, toggleToolMod, toggleToolKey, fadeMode);
    }

    private static bool IsValidHexColor(string? color)
    {
        if (color is null || color.Length != 6) return false;
        foreach (var c in color)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }
}
