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
    private const int DefaultToggleToolKey = 0x20; // VK_SPACE
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
    private const string DefaultBoxColor = "00A651";
    private const double DefaultBoxLineThickness = 3.0;
    private const string DefaultHighlightColor = "FFC90E";
    private const double DefaultHighlightOpacity = 0.35;
    private const string DefaultStepsFontFamily = "MS Reference Sans Serif, Roboto, MS UI Gothic";
    private const double DefaultStepsFontSize = 14.0;
    private const StepsShape DefaultStepsShape = StepsShape.Teardrop;
    private const bool DefaultStepsOutlineEnabled = true;
    private const double DefaultStepsSize = 36.0;
    private const string DefaultStepsFillColor = "3F48CC";
    private const string DefaultStepsOutlineColor = "FFFFFF";
    private const bool DefaultStepsFontBold = true;
    private const string DefaultStepsFontColor = "FFFFFF";
    private const AnchorEdge DefaultNubAnchorEdge = AnchorEdge.Right;
    private const string DefaultNubMonitorFingerprint = "";
    private const bool DefaultShowToolNameOnSwitch = true;
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
    public string BoxColor { get; set; } = DefaultBoxColor;
    public double BoxLineThickness { get; set; } = DefaultBoxLineThickness;
    public string HighlightColor { get; set; } = DefaultHighlightColor;
    public double HighlightOpacity { get; set; } = DefaultHighlightOpacity;
    public string StepsFontFamily { get; set; } = DefaultStepsFontFamily;
    public double StepsFontSize { get; set; } = DefaultStepsFontSize;
    public StepsShape StepsShape { get; set; } = DefaultStepsShape;
    public bool StepsOutlineEnabled { get; set; } = DefaultStepsOutlineEnabled;
    public double StepsSize { get; set; } = DefaultStepsSize;
    public string StepsFillColor { get; set; } = DefaultStepsFillColor;
    public string StepsOutlineColor { get; set; } = DefaultStepsOutlineColor;
    public bool StepsFontBold { get; set; } = DefaultStepsFontBold;
    public string StepsFontColor { get; set; } = DefaultStepsFontColor;
    public double? NubFraction { get; set; } = null;
    public AnchorEdge NubAnchorEdge { get; set; } = DefaultNubAnchorEdge;
    public string NubMonitorFingerprint { get; set; } = DefaultNubMonitorFingerprint;
    public bool ShowToolNameOnSwitch { get; set; } = DefaultShowToolNameOnSwitch;

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

    public void ResetGeneralSettings()
    {
        DragStyle = DefaultDragStyle;
        FreezeScreen = DefaultFreezeScreen;
        FadeMode = DefaultFadeMode;
        ActivationModifier = DefaultActivationModifier;
        ActivationKey = DefaultActivationKey;
        ToggleModifier = DefaultToggleModifier;
        ToggleKey = DefaultToggleKey;
        ToggleToolModifier = DefaultToggleToolModifier;
        ToggleToolKey = DefaultToggleToolKey;
        Save();
    }

    public void ResetSpotlightSettings()
    {
        OverlayOpacity = DefaultOverlayOpacity;
        FeatherRadius = DefaultFeatherRadius;
        PreviewStyle = DefaultPreviewStyle;
        CumulativeSpotlights = DefaultCumulativeSpotlights;
        CustomColors = DefaultCustomColors;
        Save();
    }

    public void ResetArrowSettings()
    {
        ArrowheadStyle = DefaultArrowheadStyle;
        ArrowEndStyle = DefaultArrowEndStyle;
        ArrowLineStyle = DefaultArrowLineStyle;
        ArrowColor = DefaultArrowColor;
        ArrowLeftEndSize = DefaultArrowLeftEndSize;
        ArrowLineThickness = DefaultArrowLineThickness;
        ArrowRightEndSize = DefaultArrowRightEndSize;
        SyncArrowEndStyle = DefaultSyncArrowEndStyle;
        SyncArrowEndSize = DefaultSyncArrowEndSize;
        Save();
    }

    public void ResetBoxSettings()
    {
        BoxColor = DefaultBoxColor;
        BoxLineThickness = DefaultBoxLineThickness;
        Save();
    }

    public void ResetHighlightSettings()
    {
        HighlightColor = DefaultHighlightColor;
        HighlightOpacity = DefaultHighlightOpacity;
        Save();
    }

    public void ResetStepsSettings()
    {
        StepsFontFamily = DefaultStepsFontFamily;
        StepsFontSize = DefaultStepsFontSize;
        StepsShape = DefaultStepsShape;
        StepsOutlineEnabled = DefaultStepsOutlineEnabled;
        StepsSize = DefaultStepsSize;
        StepsFillColor = DefaultStepsFillColor;
        StepsOutlineColor = DefaultStepsOutlineColor;
        StepsFontBold = DefaultStepsFontBold;
        StepsFontColor = DefaultStepsFontColor;
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
        ToggleToolKey = DefaultToggleToolKey;
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
        BoxColor = DefaultBoxColor;
        BoxLineThickness = DefaultBoxLineThickness;
        HighlightColor = DefaultHighlightColor;
        HighlightOpacity = DefaultHighlightOpacity;
        StepsFontFamily = DefaultStepsFontFamily;
        StepsFontSize = DefaultStepsFontSize;
        StepsShape = DefaultStepsShape;
        StepsOutlineEnabled = DefaultStepsOutlineEnabled;
        StepsSize = DefaultStepsSize;
        StepsFillColor = DefaultStepsFillColor;
        StepsOutlineColor = DefaultStepsOutlineColor;
        StepsFontBold = DefaultStepsFontBold;
        StepsFontColor = DefaultStepsFontColor;
        NubFraction = null;
        NubAnchorEdge = DefaultNubAnchorEdge;
        NubMonitorFingerprint = DefaultNubMonitorFingerprint;
        ShowToolNameOnSwitch = DefaultShowToolNameOnSwitch;
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
        BoxColor = v.BoxColor;
        BoxLineThickness = v.BoxLineThickness;
        HighlightColor = v.HighlightColor;
        HighlightOpacity = v.HighlightOpacity;
        StepsFontFamily = v.StepsFontFamily;
        StepsFontSize = v.StepsFontSize;
        StepsShape = v.StepsShape;
        StepsOutlineEnabled = v.StepsOutlineEnabled;
        StepsSize = v.StepsSize;
        StepsFillColor = v.StepsFillColor;
        StepsOutlineColor = v.StepsOutlineColor;
        StepsFontBold = v.StepsFontBold;
        StepsFontColor = v.StepsFontColor;
        NubFraction = v.NubFraction;
        NubAnchorEdge = v.NubAnchorEdge;
        NubMonitorFingerprint = v.NubMonitorFingerprint;
        ShowToolNameOnSwitch = v.ShowToolNameOnSwitch;
    }

    public void Save()
    {
        var json = Serialize(new AppSettings(OverlayOpacity, FeatherRadius, PreviewStyle, DragStyle, FreezeScreen, ActivationModifier, ActivationKey, ToggleModifier, ToggleKey, CumulativeSpotlights, ToolbarAnchorEdge, FlyoutToolbarVisible, ArrowheadStyle, ArrowEndStyle, ArrowLineStyle, ArrowColor, ArrowLeftEndSize, ArrowLineThickness, ArrowRightEndSize, SyncArrowEndStyle, SyncArrowEndSize, CustomColors, ToggleToolModifier, ToggleToolKey, FadeMode, BoxColor, BoxLineThickness, HighlightColor, HighlightOpacity, StepsFontFamily, StepsFontSize, StepsShape, StepsOutlineEnabled, StepsSize, StepsFillColor, StepsOutlineColor, StepsFontBold, StepsFontColor, NubFraction, NubAnchorEdge, NubMonitorFingerprint, ShowToolNameOnSwitch));
        File.WriteAllText(_settingsFilePath, json);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json)
        ?? new AppSettings(DefaultOverlayOpacity, DefaultFeatherRadius, DefaultPreviewStyle, DefaultDragStyle, DefaultFreezeScreen, DefaultActivationModifier, DefaultActivationKey, DefaultToggleModifier, DefaultToggleKey, DefaultCumulativeSpotlights, DefaultToolbarAnchorEdge, DefaultFlyoutToolbarVisible, DefaultArrowheadStyle, DefaultArrowEndStyle, DefaultArrowLineStyle, DefaultArrowColor, DefaultArrowLeftEndSize, DefaultArrowLineThickness, DefaultArrowRightEndSize, DefaultSyncArrowEndStyle, DefaultSyncArrowEndSize, DefaultCustomColors, DefaultToggleToolModifier, DefaultToggleToolKey, DefaultFadeMode, DefaultBoxColor, DefaultBoxLineThickness, DefaultHighlightColor, DefaultHighlightOpacity, DefaultStepsFontFamily, DefaultStepsFontSize, DefaultStepsShape, DefaultStepsOutlineEnabled, DefaultStepsSize, DefaultStepsFillColor, DefaultStepsOutlineColor, DefaultStepsFontBold, DefaultStepsFontColor, null, DefaultNubAnchorEdge, DefaultNubMonitorFingerprint, DefaultShowToolNameOnSwitch);

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
        var boxColor = IsValidHexColor(s.BoxColor) ? s.BoxColor.ToUpperInvariant() : DefaultBoxColor;
        var boxLineThick = (double.IsNaN(s.BoxLineThickness) || double.IsInfinity(s.BoxLineThickness))
            ? DefaultBoxLineThickness : Math.Clamp(s.BoxLineThickness, 1.0, 12.0);
        var highlightColor = IsValidHexColor(s.HighlightColor) ? s.HighlightColor.ToUpperInvariant() : DefaultHighlightColor;
        var highlightOpacity = (double.IsNaN(s.HighlightOpacity) || double.IsInfinity(s.HighlightOpacity))
            ? DefaultHighlightOpacity : Math.Clamp(s.HighlightOpacity, 0.1, 0.9);
        var stepsFontFamily = string.IsNullOrEmpty(s.StepsFontFamily) ? DefaultStepsFontFamily : s.StepsFontFamily;
        var stepsFontSize = (double.IsNaN(s.StepsFontSize) || double.IsInfinity(s.StepsFontSize) || s.StepsFontSize < 8 || s.StepsFontSize > 48)
            ? DefaultStepsFontSize : s.StepsFontSize;
        var stepsShape = Enum.IsDefined(s.StepsShape) ? s.StepsShape : DefaultStepsShape;
        var stepsSize = (double.IsNaN(s.StepsSize) || double.IsInfinity(s.StepsSize))
            ? DefaultStepsSize : Math.Clamp(s.StepsSize, 16, 72);
        var stepsFillColor = IsValidHexColor(s.StepsFillColor) ? s.StepsFillColor.ToUpperInvariant() : DefaultStepsFillColor;
        var stepsOutlineColor = IsValidHexColor(s.StepsOutlineColor) ? s.StepsOutlineColor.ToUpperInvariant() : DefaultStepsOutlineColor;
        var stepsFontColor = IsValidHexColor(s.StepsFontColor) ? s.StepsFontColor.ToUpperInvariant() : DefaultStepsFontColor;
        var nubFraction = s.NubFraction.HasValue ? Math.Clamp(s.NubFraction.Value, 0.0, 1.0) : (double?)null;
        var nubAnchorEdge = Enum.IsDefined(s.NubAnchorEdge) ? s.NubAnchorEdge : DefaultNubAnchorEdge;
        var nubMonitorFingerprint = s.NubMonitorFingerprint ?? DefaultNubMonitorFingerprint;
        return new AppSettings(opacity, radius, preview, drag, s.FreezeScreen, modifier, activationKey, toggleMod, toggleKey, s.CumulativeSpotlights, anchorEdge, s.FlyoutToolbarVisible, arrowheadStyle, arrowEndStyle, arrowLineStyle, arrowColor, leftSize, lineThick, rightSize, s.SyncArrowEndStyle, s.SyncArrowEndSize, s.CustomColors ?? DefaultCustomColors, toggleToolMod, toggleToolKey, fadeMode, boxColor, boxLineThick, highlightColor, highlightOpacity, stepsFontFamily, stepsFontSize, stepsShape, s.StepsOutlineEnabled, stepsSize, stepsFillColor, stepsOutlineColor, s.StepsFontBold, stepsFontColor, nubFraction, nubAnchorEdge, nubMonitorFingerprint, s.ShowToolNameOnSwitch);
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
