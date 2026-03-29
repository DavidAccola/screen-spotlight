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
    private const ModifierKey DefaultActivationModifier = ModifierKey.Ctrl;
    private const int DefaultActivationKey = 0; // 0 = no key, modifier-only
    private const ModifierKey DefaultToggleModifier = ModifierKey.CtrlShift;
    private const int DefaultToggleKey = 0x51; // VK_Q
    private const bool DefaultCumulativeSpotlights = true;
    private const AnchorEdge DefaultToolbarAnchorEdge = AnchorEdge.Right;
    private const bool DefaultFlyoutToolbarVisible = true;
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
    public bool CumulativeSpotlights { get; set; } = DefaultCumulativeSpotlights;
    public AnchorEdge ToolbarAnchorEdge { get; set; } = DefaultToolbarAnchorEdge;
    public bool FlyoutToolbarVisible { get; set; } = DefaultFlyoutToolbarVisible;

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
        CumulativeSpotlights = DefaultCumulativeSpotlights;
        ToolbarAnchorEdge = DefaultToolbarAnchorEdge;
        FlyoutToolbarVisible = DefaultFlyoutToolbarVisible;
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
        CumulativeSpotlights = v.CumulativeSpotlights;
        ToolbarAnchorEdge = v.ToolbarAnchorEdge;
        FlyoutToolbarVisible = v.FlyoutToolbarVisible;
    }

    public void Save()
    {
        var json = Serialize(new AppSettings(OverlayOpacity, FeatherRadius, PreviewStyle, DragStyle, FreezeScreen, ActivationModifier, ActivationKey, ToggleModifier, ToggleKey, CumulativeSpotlights, ToolbarAnchorEdge, FlyoutToolbarVisible));
        File.WriteAllText(_settingsFilePath, json);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json)
        ?? new AppSettings(DefaultOverlayOpacity, DefaultFeatherRadius, DefaultPreviewStyle, DefaultDragStyle, DefaultFreezeScreen, DefaultActivationModifier, DefaultActivationKey, DefaultToggleModifier, DefaultToggleKey, DefaultCumulativeSpotlights, DefaultToolbarAnchorEdge, DefaultFlyoutToolbarVisible);

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
        return new AppSettings(opacity, radius, preview, drag, s.FreezeScreen, modifier, activationKey, toggleMod, toggleKey, s.CumulativeSpotlights, anchorEdge, s.FlyoutToolbarVisible);
    }
}
