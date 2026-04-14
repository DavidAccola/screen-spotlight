using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SpotlightOverlay.Input;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using WPoint = System.Windows.Point;
using WButton = System.Windows.Controls.Button;

namespace SpotlightOverlay.Windows;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private readonly SettingsService _settings;
    private readonly GlobalInputHook? _inputHook;
    private bool _isInitializing;
    private System.Windows.Threading.DispatcherTimer? _animTimer;
    private int _animFrame;

    // HSV picker state (moved to ColorPickerDialog)
    // Custom color slots state
    private static readonly (string Hex, string Name)[] PresetColors =
    {
        // Row 1: dark/vivid colors (MS Paint top row)
        ("000000", "Black"),   ("7F7F7F", "Gray"),    ("880015", "Dark Red"), ("FF0000", "Red"),
        ("FF7F27", "Orange"),  ("FFFF00", "Yellow"),   ("00A651", "Green"),    ("00A2E8", "Sky Blue"),
        ("3F48CC", "Blue"),    ("A349A4", "Purple"),
        // Row 2: light/pastel colors (MS Paint bottom row)
        ("FFFFFF", "White"),   ("C3C3C3", "Light Gray"),("B97A57", "Brown"),   ("FFAEC9", "Pink"),
        ("FFC90E", "Gold"),    ("EFE4B0", "Tan"),      ("B5E61D", "Lime"),     ("99D9EA", "Light Blue"),
        ("7092BE", "Slate"),   ("C8BFE7", "Lavender"),
    };

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT   = 0x10;
    private const int VK_MENU    = 0x12; // Alt

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Read the physical modifier state via GetAsyncKeyState — more reliable
    /// than Keyboard.Modifiers during rapid key combos in PreviewKeyDown.
    /// </summary>
    private static Models.ModifierKey? ReadPhysicalModifiers()
    {
        bool ctrl  = IsKeyDown(VK_CONTROL);
        bool shift = IsKeyDown(VK_SHIFT);
        bool alt   = IsKeyDown(VK_MENU);

        if (ctrl && shift) return Models.ModifierKey.CtrlShift;
        if (ctrl && alt)   return Models.ModifierKey.CtrlAlt;
        if (ctrl)          return Models.ModifierKey.Ctrl;
        if (alt)           return Models.ModifierKey.Alt;
        if (shift)         return Models.ModifierKey.Shift;
        return null;
    }

    public SettingsWindow(SettingsService settings, GlobalInputHook? inputHook = null)
    {
        _settings = settings;
        _inputHook = inputHook;
        _isInitializing = true;
        InitializeComponent();

        // Force dark title bar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
        if (new WindowInteropHelper(this).EnsureHandle() is var hwnd && hwnd != IntPtr.Zero)
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));

            // Match title bar color to window background (#1E1E1E) — DWMWA_CAPTION_COLOR = 35
            int captionColor = 0x1E1E1E; // COLORREF: 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
        }

        OpacitySlider.Value = 100 - _settings.OverlayOpacity * 100;
        OpacityTextBox.Text = ((int)(100 - _settings.OverlayOpacity * 100)).ToString() + "%";
        FeatherSlider.Value = _settings.FeatherRadius;
        FeatherTextBox.Text = _settings.FeatherRadius.ToString();
        PreviewStyleCombo.SelectedIndex = (int)_settings.PreviewStyle;
        DragStyleCombo.SelectedIndex = (int)_settings.DragStyle;
        BackgroundCombo.SelectedIndex = _settings.FreezeScreen ? 1 : 0;
        FadeModeCombo.SelectedIndex = (int)_settings.FadeMode;
        EscBehaviorCombo.SelectedIndex = (int)_settings.EscBehavior;
        SpotlightModeCombo.SelectedIndex = _settings.CumulativeSpotlights ? 0 : 1;
        NestedSpotlightModeCombo.SelectedIndex = (int)_settings.NestedSpotlightMode;
        ArrowheadStyleCombo_Init();
        BuildColorPresetSwatches();
        LoadCustomColors();
        BuildCustomColorSlots();
        InitSizeControls();
        SyncStyleCheck_Init();
        SyncSizeCheck_Init();
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        UpdateToggleToolHotkeyDisplay();
        UpdateDragStyleLabels();

        BoxSizeSlider.Value = _settings.BoxLineThickness;
        BoxSizeTextBox.Text = ((int)_settings.BoxLineThickness).ToString();
        BuildBoxColorPresetSwatches();
        BuildBoxCustomColorSlots();

        HighlightOpacitySlider.Value = (int)(_settings.HighlightOpacity * 100);
        HighlightOpacityTextBox.Text = ((int)(_settings.HighlightOpacity * 100)).ToString() + "%";
        BuildHighlightColorPresetSwatches();
        BuildHighlightCustomColorSlots();

        // Steps tab initialization
        BuildStepsFontFamilyCombo();
        StepsFontSizeTextBox.Text = ((int)_settings.StepsFontSize).ToString();
        StepsSizeSlider.Value = _settings.StepsSize;
        StepsSizeTextBox.Text = ((int)_settings.StepsSize).ToString();
        StepsOutlineEnabledCheck.IsChecked = _settings.StepsOutlineEnabled;
        StepsTailDirectionCombo.SelectedIndex = (int)_settings.StepsTailDirection;
        HighlightStepsShapeToggle();
        HighlightStepsBoldBtn();
        HighlightStepsColorTab();
        BuildStepsFillColorPresetSwatches();
        BuildStepsFillCustomColorSlots();
        BuildStepsOutlineColorPresetSwatches();
        BuildStepsOutlineCustomColorSlots();
        BuildStepsFontColorPresetSwatches();
        BuildStepsFontCustomColorSlots();

        _isInitializing = false;
        UpdateSpotlightModeHint();
        UpdateNestedSpotlightModeHint();
        UpdateNestedSpotlightModeVisibility();
        InitToolbarTab();
        Loaded += (_, _) =>
        {
            UpdatePreview(); UpdateArrowPreview(); UpdateBoxPreview(); UpdateHighlightPreview();
            UpdateStepsPreview();
            ArrowPreviewArea.SizeChanged += (_, _) => UpdateArrowPreview();
            BoxPreviewArea.SizeChanged += (_, _) => UpdateBoxPreview();
            HighlightPreviewArea.SizeChanged += (_, _) => UpdateHighlightPreview();
            StepsPreviewArea.SizeChanged += (_, _) => UpdateStepsPreview();
        };
    }

    public static void ShowSingleton(SettingsService settings, GlobalInputHook? inputHook = null)
    {
        if (_instance is { IsLoaded: true }) { _instance.Activate(); return; }
        _instance = new SettingsWindow(settings, inputHook);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    // ── Preview constants ──────────────────────────────────────────

    private const double CanvasW = 486, CanvasH = 110;
    private static readonly Rect CutoutFinal = new(
        CanvasW * 0.3, CanvasH * 0.25, CanvasW * 0.4, CanvasH * 0.5);

    private const int DragFrames = 12;
    private const int HoldFrames = 10;
    private const int FrameMs = 33;

    // ── Preview rendering ──────────────────────────────────────────

    private void UpdatePreview(bool animateStyle = false)
    {
        if (PreviewArea == null) return;
        StopAnimation();

        if (animateStyle)
        {
            _animFrame = 0;
            _animTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(FrameMs)
            };
            _animTimer.Tick += StyleAnimTick;
            _animTimer.Start();
            return;
        }

        DrawStaticPreview();
    }

    private void DrawStaticPreview()
    {
        PreviewArea.Children.Clear();
        DrawFeatheredOverlay(CutoutFinal);
        DrawStyleIndicators(CutoutFinal);
    }

    private void StyleAnimTick(object? sender, EventArgs e)
    {
        _animFrame++;
        PreviewArea.Children.Clear();

        if (_animFrame <= DragFrames)
        {
            double t = (double)_animFrame / DragFrames;
            double w = CutoutFinal.Width * t;
            double h = CutoutFinal.Height * t;
            var growing = new Rect(CutoutFinal.X, CutoutFinal.Y, Math.Max(2, w), Math.Max(2, h));

            // Dark background with overscan so edges are hidden beyond the clipped canvas
            double overscan = 30;
            var full = new RectangleGeometry(new Rect(-overscan, -overscan, CanvasW + overscan * 2, CanvasH + overscan * 2));
            PreviewArea.Children.Add(new System.Windows.Shapes.Path
            {
                Data = full,
                Fill = new SolidColorBrush(Color.FromArgb(
                    (byte)(_settings.OverlayOpacity * 255), 0, 0, 0)),
                IsHitTestVisible = false
            });

            DrawStyleIndicators(growing);
        }
        else if (_animFrame <= DragFrames + HoldFrames)
        {
            DrawFeatheredOverlay(CutoutFinal);
            DrawStyleIndicators(CutoutFinal);
        }
        else
        {
            StopAnimation();
            DrawStaticPreview();
        }
    }

    private void StopAnimation()
    {
        if (_animTimer != null)
        {
            _animTimer.Stop();
            _animTimer.Tick -= StyleAnimTick;
            _animTimer = null;
        }
    }

    // ── Drawing helpers ────────────────────────────────────────────

    private void DrawFeatheredOverlay(Rect cutout)
    {
        double opacity = _settings.OverlayOpacity;
        int feather = _settings.FeatherRadius;

        if (feather <= 0)
        {
            double overscan = 30;
            var full = new RectangleGeometry(new Rect(-overscan, -overscan, CanvasW + overscan * 2, CanvasH + overscan * 2));
            var hole = new RectangleGeometry(cutout);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
            PreviewArea.Children.Add(new System.Windows.Shapes.Path
            {
                Data = combined,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0, 0, 0)),
                IsHitTestVisible = false
            });
            return;
        }

        // Use the feather value directly — the preview is small enough that even
        // max feather (50) produces a visible, representative soft edge.
        double featherScaled = feather;

        double pad = Math.Max(30, featherScaled * 3);
        double totalW = CanvasW + pad * 2;
        double totalH = CanvasH + pad * 2;

        // Render at half resolution for speed — blur hides the lower resolution
        const double scale = 0.5;
        int w = Math.Max(1, (int)(totalW * scale));
        int h = Math.Max(1, (int)(totalH * scale));
        int blurRadius = Math.Max(1, (int)(featherScaled * scale));

        double expand = featherScaled * 0.5;
        var scaledCutout = new Rect(
            (cutout.X - expand + pad) * scale,
            (cutout.Y - expand + pad) * scale,
            (cutout.Width + expand * 2) * scale,
            (cutout.Height + expand * 2) * scale);

        var maskEl = new MaskElement(scaledCutout, w, h) { Width = w, Height = h };
        if (blurRadius > 0)
        {
            maskEl.Effect = new BlurEffect
            {
                Radius = blurRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            };
        }
        maskEl.Measure(new Size(w, h));
        maskEl.Arrange(new Rect(0, 0, w, h));

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(maskEl);

        var darkRect = new Rectangle
        {
            Width = totalW, Height = totalH,
            Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0, 0, 0)),
            OpacityMask = new ImageBrush(rtb) { Stretch = Stretch.Fill },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(darkRect, -pad);
        Canvas.SetTop(darkRect, -pad);
        PreviewArea.Children.Add(darkRect);
    }

    private class MaskElement : FrameworkElement
    {
        private readonly Rect _cutout;
        private readonly int _w, _h;
        public MaskElement(Rect cutout, int w, int h) { _cutout = cutout; _w = w; _h = h; }
        protected override void OnRender(DrawingContext dc)
        {
            Geometry mask = new RectangleGeometry(new Rect(0, 0, _w, _h));
            mask = new CombinedGeometry(GeometryCombineMode.Exclude, mask, new RectangleGeometry(_cutout));
            dc.DrawGeometry(System.Windows.Media.Brushes.White, null, mask);
        }
    }

    private void DrawStyleIndicators(Rect cutout)
    {
        var style = _settings.PreviewStyle;
        double cornerLen = 10;

        if (style == Models.PreviewStyle.Outline)
        {
            var shadowOutline = new Rectangle
            {
                Width = cutout.Width, Height = cutout.Height,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
                StrokeThickness = 2, IsHitTestVisible = false
            };
            Canvas.SetLeft(shadowOutline, cutout.X);
            Canvas.SetTop(shadowOutline, cutout.Y);
            PreviewArea.Children.Add(shadowOutline);

            var fgOutline = new Rectangle
            {
                Width = cutout.Width, Height = cutout.Height,
                Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
            Canvas.SetLeft(fgOutline, cutout.X);
            Canvas.SetTop(fgOutline, cutout.Y);
            PreviewArea.Children.Add(fgOutline);
        }
        else
        {
            bool halfOnly = style == Models.PreviewStyle.Corners;
            var shadow = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0));
            var fg = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

            AddCropMark(shadow, fg, cornerLen, cutout.Left, cutout.Top, 1, 1);
            AddCropMark(shadow, fg, cornerLen, cutout.Right, cutout.Bottom, -1, -1);
            if (!halfOnly)
            {
                AddCropMark(shadow, fg, cornerLen, cutout.Right, cutout.Top, -1, 1);
                AddCropMark(shadow, fg, cornerLen, cutout.Left, cutout.Bottom, 1, -1);
            }
        }
    }

    private void AddCropMark(SolidColorBrush shadow, SolidColorBrush fg,
        double len, double cx, double cy, int dx, int dy)
    {
        AddLine(shadow, 2, cx, cy, cx + len * dx, cy);
        AddLine(fg, 1, cx, cy, cx + len * dx, cy);
        AddLine(shadow, 2, cx, cy, cx, cy + len * dy);
        AddLine(fg, 1, cx, cy, cx, cy + len * dy);
    }

    private void DrawDragStyleLabel()
    {
        string mod = ModifierDisplayName(_settings.ActivationModifier);
        string keyName = _settings.ActivationKey != 0 ? VkDisplayName(_settings.ActivationKey) : "";
        bool isMouseBtn = IsMouseButtonVk(_settings.ActivationKey);

        // Build prefix: "Ctrl+Space", "Mouse 4", "Ctrl", etc.
        string prefix;
        if (!string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(keyName))
            prefix = mod + "+" + keyName;
        else if (!string.IsNullOrEmpty(keyName))
            prefix = keyName;
        else
            prefix = mod;

        string label;
        if (isMouseBtn)
        {
            // Mouse button IS the click — no extra "+Drag"/"+Click"
            label = _settings.DragStyle == Models.DragStyle.HoldDrag
                ? $"{prefix}+Drag"
                : $"{prefix} > {prefix}";
        }
        else
        {
            label = _settings.DragStyle == Models.DragStyle.HoldDrag
                ? $"{prefix}+Drag"
                : $"{prefix}+Click > Click";
        }

        bool dark = _settings.OverlayOpacity <= 0.3;
        var tb = new TextBlock
        {
            Text = label,
            Foreground = dark ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White,
            FontSize = 9, Opacity = dark ? 0.6 : 0.5, IsHitTestVisible = false
        };
        double areaW = PreviewArea.ActualWidth > 0 ? PreviewArea.ActualWidth : CanvasW;
        tb.Measure(new Size(areaW, CanvasH));
        Canvas.SetLeft(tb, areaW - tb.DesiredSize.Width - 14);
        Canvas.SetTop(tb, CanvasH - tb.DesiredSize.Height - 6);
        PreviewArea.Children.Add(tb);
    }

    private void AddLine(System.Windows.Media.Brush stroke, double thickness,
        double x1, double y1, double x2, double y2)
    {
        PreviewArea.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false
        });
    }

    // ── Settings event handlers ────────────────────────────────────

    private void UpdateDragStyleLabels()
    {
        string mod = ModifierDisplayName(_settings.ActivationModifier);
        string keyName = _settings.ActivationKey != 0 ? VkDisplayName(_settings.ActivationKey) : "";
        bool isMouseBtn = IsMouseButtonVk(_settings.ActivationKey);

        string prefix;
        if (!string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(keyName))
            prefix = mod + " + " + keyName;
        else if (!string.IsNullOrEmpty(keyName))
            prefix = keyName;
        else
            prefix = mod;

        if (isMouseBtn)
        {
            DragStyleHold.Content = $"{prefix} + Hold and Drag";
            DragStyleClick.Content = $"{prefix} & Click Again";
        }
        else
        {
            DragStyleHold.Content = $"{prefix} + Hold and Drag";
            DragStyleClick.Content = $"{prefix} + Click & Click Again";
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        var transPct = (int)Math.Clamp(e.NewValue, 1, 99);
        _settings.OverlayOpacity = (100 - transPct) / 100.0;
        _settings.Save();
        OpacityTextBox.Text = transPct.ToString() + "%";
        UpdatePreview();
    }

    private void ApplyOpacityFromTextBox()
    {
        var text = OpacityTextBox.Text.TrimEnd('%', ' ');
        if (int.TryParse(text, out int transPct))
        {
            transPct = Math.Clamp(transPct, 1, 99);
            _isInitializing = true;
            OpacitySlider.Value = transPct;
            _isInitializing = false;
            _settings.OverlayOpacity = (100 - transPct) / 100.0;
            _settings.Save();
            OpacityTextBox.Text = transPct.ToString() + "%";
            UpdatePreview();
        }
        else
        {
            OpacityTextBox.Text = ((int)(100 - _settings.OverlayOpacity * 100)).ToString() + "%";
        }
    }

    private void OpacityTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyOpacityFromTextBox();
    private void OpacityTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplyOpacityFromTextBox();
    }

    private void OpacityUp_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp((int)OpacitySlider.Value + 10, 10, 90);
        OpacitySlider.Value = val;
    }
    private void OpacityDown_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp((int)OpacitySlider.Value - 10, 10, 90);
        OpacitySlider.Value = val;
    }

    private void FeatherSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        var val = Math.Clamp((int)e.NewValue, 0, 50);
        _settings.FeatherRadius = val;
        _settings.Save();
        FeatherTextBox.Text = val.ToString();
        UpdatePreview();
    }

    private void ApplyFeatherFromTextBox()
    {
        if (int.TryParse(FeatherTextBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 50);
            _isInitializing = true;
            FeatherSlider.Value = val;
            _isInitializing = false;
            _settings.FeatherRadius = val;
            _settings.Save();
            FeatherTextBox.Text = val.ToString();
            UpdatePreview();
        }
        else
        {
            FeatherTextBox.Text = _settings.FeatherRadius.ToString();
        }
    }

    private void FeatherTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyFeatherFromTextBox();
    private void FeatherTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplyFeatherFromTextBox();
    }

    private void FeatherUp_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp((int)FeatherSlider.Value + 1, 0, 50);
        FeatherSlider.Value = val;
    }
    private void FeatherDown_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp((int)FeatherSlider.Value - 1, 0, 50);
        FeatherSlider.Value = val;
    }

    private void PreviewStyleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.PreviewStyle = (Models.PreviewStyle)PreviewStyleCombo.SelectedIndex;
        _settings.Save();
        UpdatePreview(animateStyle: true);
    }

    private void DragStyleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.DragStyle = (Models.DragStyle)DragStyleCombo.SelectedIndex;
        _settings.Save();
        UpdatePreview();
    }

    private void BackgroundCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.FreezeScreen = BackgroundCombo.SelectedIndex == 1;
        _settings.Save();
    }

    private void FadeModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.FadeMode = (Models.FadeMode)FadeModeCombo.SelectedIndex;
        _settings.Save();
    }

    private void ShowToolNameCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.ShowToolNameOnSwitch = ShowToolNameCheck.IsChecked == true;
        _settings.Save();
    }

    private void EscBehaviorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.EscBehavior = (Models.EscBehavior)EscBehaviorCombo.SelectedIndex;
        _settings.Save();
    }

    private void SpotlightModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.CumulativeSpotlights = SpotlightModeCombo.SelectedIndex == 0;
        _settings.Save();
        UpdateSpotlightModeHint();
        UpdateNestedSpotlightModeVisibility();
    }

    private void UpdateSpotlightModeHint()
    {
        if (SpotlightModeHint == null) return;
        SpotlightModeHint.Text = _settings.CumulativeSpotlights
            ? "New spotlights are added alongside existing ones"
            : "Each new spotlight replaces the previous one";
    }

    private void UpdateNestedSpotlightModeVisibility()
    {
        if (NestedSpotlightModeGrid == null) return;
        // Only show nested mode when Additional spotlights is set to "Multiple can exist"
        NestedSpotlightModeGrid.Visibility = _settings.CumulativeSpotlights 
            ? System.Windows.Visibility.Visible 
            : System.Windows.Visibility.Collapsed;
    }

    private void NestedSpotlightModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.NestedSpotlightMode = (Models.NestedSpotlightMode)NestedSpotlightModeCombo.SelectedIndex;
        _settings.Save();
        UpdateNestedSpotlightModeHint();
    }

    private void UpdateNestedSpotlightModeHint()
    {
        if (NestedSpotlightModeHint == null) return;
        NestedSpotlightModeHint.Text = _settings.NestedSpotlightMode == Models.NestedSpotlightMode.Darken
            ? "A spotlight entirely inside another creates a layer of darkness"
            : "A spotlight entirely inside another replaces the larger spotlight";
    }

    // ── Arrow style visual combos ─────────────────────────────────

    private void ArrowheadStyleCombo_Init()
    {
        _isInitializing = true;
        PopulateEndCombo(LeftEndCombo, _settings.ArrowheadStyle, pointLeft: true);
        PopulateEndCombo(RightEndCombo, _settings.ArrowEndStyle, pointLeft: false);
        PopulateLineStyleCombo(LineStyleCombo, _settings.ArrowLineStyle);
        _isInitializing = false;
    }

    private void PopulateEndCombo(System.Windows.Controls.ComboBox combo, ArrowheadStyle selected, bool pointLeft)
    {
        combo.Items.Clear();
        // Use DataTemplate so WPF creates a fresh Image for both dropdown and selection box
        combo.ItemTemplate = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(System.Windows.Controls.Image));
        factory.SetBinding(System.Windows.Controls.Image.SourceProperty,
            new System.Windows.Data.Binding("."));
        factory.SetValue(System.Windows.Controls.Image.StretchProperty, Stretch.Uniform);
        factory.SetValue(FrameworkElement.HeightProperty, 20.0);
        combo.ItemTemplate.VisualTree = factory;

        var styles = new[] {
            ArrowheadStyle.None,
            ArrowheadStyle.FilledTriangle,
            ArrowheadStyle.OpenArrowhead,
            ArrowheadStyle.Barbed,
            ArrowheadStyle.DotEnd,
        };
        int selectedIndex = 0;
        for (int i = 0; i < styles.Length; i++)
        {
            combo.Items.Add(BuildEndDrawingImage(styles[i], pointLeft));
            if (styles[i] == selected) selectedIndex = i;
        }
        // Store style values for lookup in SelectionChanged
        combo.Tag = styles;
        combo.SelectedIndex = selectedIndex;
    }

    private static DrawingImage BuildEndDrawingImage(ArrowheadStyle style, bool pointLeft)
    {
        double w = 40, h = 20;
        double angle = pointLeft ? Math.PI : 0;
        var tip = pointLeft ? new System.Windows.Point(6, 10) : new System.Windows.Point(34, 10);
        var lineEnd = pointLeft ? new System.Windows.Point(34, 10) : new System.Windows.Point(6, 10);
        var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2) { LineJoin = PenLineJoin.Round };

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(
            System.Windows.Media.Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, w, h))));
        drawingGroup.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(lineEnd, tip)));
        var geom = Rendering.ArrowRenderer.BuildArrowheadGeometry(tip, angle, style);
        if (geom != null && !geom.IsEmpty())
        {
            System.Windows.Media.Brush? fill = (style is ArrowheadStyle.FilledTriangle or ArrowheadStyle.Barbed or ArrowheadStyle.DotEnd)
                ? System.Windows.Media.Brushes.White : null;
            drawingGroup.Children.Add(new GeometryDrawing(fill, pen, geom));
        }
        return new DrawingImage(drawingGroup);
    }

    private void PopulateLineStyleCombo(System.Windows.Controls.ComboBox combo, ArrowLineStyle selected)
    {
        combo.Items.Clear();
        combo.ItemTemplate = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(System.Windows.Controls.Image));
        factory.SetBinding(System.Windows.Controls.Image.SourceProperty,
            new System.Windows.Data.Binding("."));
        factory.SetValue(System.Windows.Controls.Image.StretchProperty, Stretch.Uniform);
        factory.SetValue(FrameworkElement.HeightProperty, 20.0);
        combo.ItemTemplate.VisualTree = factory;

        var styles = new[] { ArrowLineStyle.Solid, ArrowLineStyle.Dashed, ArrowLineStyle.Dotted };
        int selectedIndex = 0;
        for (int i = 0; i < styles.Length; i++)
        {
            combo.Items.Add(BuildLineStyleDrawingImage(styles[i]));
            if (styles[i] == selected) selectedIndex = i;
        }
        combo.Tag = styles;
        combo.SelectedIndex = selectedIndex;
    }

    private static DrawingImage BuildLineStyleDrawingImage(ArrowLineStyle style)
    {
        double w = 50, h = 20;
        var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2);
        if (style == ArrowLineStyle.Dashed)
        {
            pen.DashStyle = new DashStyle(new double[] { 5, 3 }, 0);
            pen.DashCap = PenLineCap.Flat;
        }
        else if (style == ArrowLineStyle.Dotted)
        {
            pen.DashStyle = new DashStyle(new double[] { 1, 1.5 }, 0);
            pen.DashCap = PenLineCap.Flat;
        }

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(
            System.Windows.Media.Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, w, h))));
        drawingGroup.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(
            new System.Windows.Point(4, 10), new System.Windows.Point(46, 10))));
        return new DrawingImage(drawingGroup);
    }

    private void ArrowStyleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || !IsLoaded) return;

        if (LeftEndCombo.Tag is ArrowheadStyle[] leftStyles && LeftEndCombo.SelectedIndex >= 0)
            _settings.ArrowheadStyle = leftStyles[LeftEndCombo.SelectedIndex];
        if (RightEndCombo.Tag is ArrowheadStyle[] rightStyles && RightEndCombo.SelectedIndex >= 0)
            _settings.ArrowEndStyle = rightStyles[RightEndCombo.SelectedIndex];
        if (LineStyleCombo.Tag is ArrowLineStyle[] lineStyles && LineStyleCombo.SelectedIndex >= 0)
            _settings.ArrowLineStyle = lineStyles[LineStyleCombo.SelectedIndex];

        // Sync end styles if enabled
        if (_settings.SyncArrowEndStyle && (sender == LeftEndCombo || sender == RightEndCombo))
        {
            _isInitializing = true;
            if (sender == LeftEndCombo)
            {
                _settings.ArrowEndStyle = _settings.ArrowheadStyle;
                if (RightEndCombo.Tag is ArrowheadStyle[] rs)
                    RightEndCombo.SelectedIndex = Array.IndexOf(rs, _settings.ArrowEndStyle);
            }
            else
            {
                _settings.ArrowheadStyle = _settings.ArrowEndStyle;
                if (LeftEndCombo.Tag is ArrowheadStyle[] ls)
                    LeftEndCombo.SelectedIndex = Array.IndexOf(ls, _settings.ArrowheadStyle);
            }
            _isInitializing = false;
        }

        _settings.Save();
        HighlightSizeToggle();
        UpdateSyncStyleCheckEnabled();
        UpdateSyncSizeCheckEnabled();
        UpdateArrowPreview();
    }

    // ── Size controls ──────────────────────────────────────────────

    private enum SizeTarget { LeftEnd, Line, RightEnd }
    private SizeTarget _currentSizeTarget = SizeTarget.RightEnd;

    private void InitSizeControls()
    {
        // Set visual content for toggle buttons
        SizeLeftContent.Content = new System.Windows.Controls.Image
        {
            Source = BuildEndDrawingImage(ArrowheadStyle.FilledTriangle, true),
            Stretch = Stretch.Uniform, Height = 20
        };
        SizeLineContent.Content = new System.Windows.Controls.Image
        {
            Source = BuildLineStyleDrawingImage(ArrowLineStyle.Solid),
            Stretch = Stretch.Uniform, Height = 20
        };
        SizeRightContent.Content = new System.Windows.Controls.Image
        {
            Source = BuildEndDrawingImage(ArrowheadStyle.FilledTriangle, false),
            Stretch = Stretch.Uniform, Height = 20
        };
        _currentSizeTarget = SizeTarget.RightEnd;
        HighlightSizeToggle();
        UpdateSizeSlider();
    }

    private void SizeToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border btn && btn.Tag is string tag && btn.IsEnabled)
        {
            _currentSizeTarget = tag switch
            {
                "Left" => SizeTarget.LeftEnd,
                "Line" => SizeTarget.Line,
                _ => SizeTarget.RightEnd,
            };
            HighlightSizeToggle();
            UpdateSizeSlider();
        }
    }

    private void HighlightSizeToggle()
    {
        var accent = (SolidColorBrush)FindResource("Accent");
        var normal = (SolidColorBrush)FindResource("ControlBg");
        var borderNormal = (SolidColorBrush)FindResource("CardBorder");
        var disabled = new SolidColorBrush(Color.FromArgb(0x40, 0x38, 0x38, 0x38));

        bool leftEnabled = _settings.ArrowheadStyle != ArrowheadStyle.None;
        bool rightEnabled = _settings.ArrowEndStyle != ArrowheadStyle.None;

        // If selected target is disabled, fall back to Line
        if (!leftEnabled && _currentSizeTarget == SizeTarget.LeftEnd)
        { _currentSizeTarget = SizeTarget.Line; UpdateSizeSlider(); }
        if (!rightEnabled && _currentSizeTarget == SizeTarget.RightEnd)
        { _currentSizeTarget = SizeTarget.Line; UpdateSizeSlider(); }

        SizeLeftBtn.IsEnabled = leftEnabled;
        SizeLeftBtn.Visibility = leftEnabled ? Visibility.Visible : Visibility.Hidden;
        SizeLeftBtn.Cursor = leftEnabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        SizeRightBtn.IsEnabled = rightEnabled;
        SizeRightBtn.Visibility = rightEnabled ? Visibility.Visible : Visibility.Hidden;
        SizeRightBtn.Cursor = rightEnabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;

        // When size sync is on, both end buttons highlight together
        bool syncSize = _settings.SyncArrowEndSize && leftEnabled && rightEnabled;
        bool leftSelected = _currentSizeTarget == SizeTarget.LeftEnd || (syncSize && _currentSizeTarget == SizeTarget.RightEnd);
        bool rightSelected = _currentSizeTarget == SizeTarget.RightEnd || (syncSize && _currentSizeTarget == SizeTarget.LeftEnd);

        SizeLeftBtn.Background = (leftSelected && leftEnabled) ? accent : normal;
        SizeLeftBtn.BorderBrush = (leftSelected && leftEnabled) ? accent : borderNormal;
        SizeLineBtn.Background = _currentSizeTarget == SizeTarget.Line ? accent : normal;
        SizeLineBtn.BorderBrush = _currentSizeTarget == SizeTarget.Line ? accent : borderNormal;
        SizeRightBtn.Background = (rightSelected && rightEnabled) ? accent : normal;
        SizeRightBtn.BorderBrush = (rightSelected && rightEnabled) ? accent : borderNormal;
    }

    private void SizeRadio_Checked(object sender, RoutedEventArgs e) { }

    private void UpdateSizeSlider()
    {
        if (SizeSlider == null) return;
        _isInitializing = true;
        switch (_currentSizeTarget)
        {
            case SizeTarget.LeftEnd:
                SizeSlider.Minimum = 8; SizeSlider.Maximum = 60;
                SizeSlider.Value = _settings.ArrowLeftEndSize;
                break;
            case SizeTarget.Line:
                SizeSlider.Minimum = 1; SizeSlider.Maximum = 12;
                SizeSlider.Value = _settings.ArrowLineThickness;
                break;
            case SizeTarget.RightEnd:
                SizeSlider.Minimum = 8; SizeSlider.Maximum = 60;
                SizeSlider.Value = _settings.ArrowRightEndSize;
                break;
        }
        if (SizeTextBox != null)
            SizeTextBox.Text = ((int)SizeSlider.Value).ToString();
        _isInitializing = false;
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || !IsLoaded) return;
        switch (_currentSizeTarget)
        {
            case SizeTarget.LeftEnd:
                _settings.ArrowLeftEndSize = e.NewValue;
                if (_settings.SyncArrowEndSize) _settings.ArrowRightEndSize = e.NewValue;
                break;
            case SizeTarget.Line:
                _settings.ArrowLineThickness = e.NewValue;
                break;
            case SizeTarget.RightEnd:
                _settings.ArrowRightEndSize = e.NewValue;
                if (_settings.SyncArrowEndSize) _settings.ArrowLeftEndSize = e.NewValue;
                break;
        }
        _settings.Save();
        if (SizeTextBox != null)
            SizeTextBox.Text = ((int)e.NewValue).ToString();
        UpdateArrowPreview();
    }

    private void SizeTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplySizeFromTextBox();
    private void SizeTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplySizeFromTextBox();
    }

    private void SizeUp_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp(SizeSlider.Value + 2, SizeSlider.Minimum, SizeSlider.Maximum);
        SizeSlider.Value = val;
    }
    private void SizeDown_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp(SizeSlider.Value - 2, SizeSlider.Minimum, SizeSlider.Maximum);
        SizeSlider.Value = val;
    }

    private void ApplySizeFromTextBox()
    {
        if (int.TryParse(SizeTextBox.Text, out int val))
        {
            val = (int)Math.Clamp(val, SizeSlider.Minimum, SizeSlider.Maximum);
            _isInitializing = true;
            SizeSlider.Value = val;
            _isInitializing = false;
            switch (_currentSizeTarget)
            {
                case SizeTarget.LeftEnd: _settings.ArrowLeftEndSize = val; break;
                case SizeTarget.Line: _settings.ArrowLineThickness = val; break;
                case SizeTarget.RightEnd: _settings.ArrowRightEndSize = val; break;
            }
            _settings.Save();
            SizeTextBox.Text = val.ToString();
            UpdateArrowPreview();
        }
        else
        {
            SizeTextBox.Text = ((int)SizeSlider.Value).ToString();
        }
    }

    private bool _isRecordingHotkey;

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConfirmResetDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _settings.ResetToDefaults();
        _isInitializing = true;
        RefreshGeneralTabUI();
        RefreshSpotlightTabUI();
        RefreshArrowTabUI();
        RefreshBoxTabUI();
        RefreshHighlightTabUI();
        RefreshStepsTabUI();
        _isInitializing = false;
        UpdateSpotlightModeHint();
        UpdatePreview();
        UpdateArrowPreview();
        UpdateBoxPreview();
        UpdateHighlightPreview();
        UpdateStepsPreview();
    }

    private void ResetGeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetGeneralSettings();
        _isInitializing = true;
        RefreshGeneralTabUI();
        _isInitializing = false;
        UpdatePreview();
    }

    private void ResetSpotlightSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetSpotlightSettings();
        _isInitializing = true;
        RefreshSpotlightTabUI();
        _isInitializing = false;
        UpdateSpotlightModeHint();
        UpdatePreview();
    }

    private void ResetArrowSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetArrowSettings();
        _isInitializing = true;
        RefreshArrowTabUI();
        _isInitializing = false;
        UpdateArrowPreview();
    }

    private void ResetBoxSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetBoxSettings();
        _isInitializing = true;
        RefreshBoxTabUI();
        _isInitializing = false;
        UpdateBoxPreview();
    }

    private void ResetHighlightSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetHighlightSettings();
        _isInitializing = true;
        RefreshHighlightTabUI();
        _isInitializing = false;
        UpdateHighlightPreview();
    }

    private void ResetStepsSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetStepsSettings();
        _isInitializing = true;
        RefreshStepsTabUI();
        _isInitializing = false;
        UpdateStepsPreview();
    }

    // ── Per-tab UI refresh helpers ─────────────────────────────────

    private void RefreshGeneralTabUI()
    {
        DragStyleCombo.SelectedIndex = (int)_settings.DragStyle;
        BackgroundCombo.SelectedIndex = _settings.FreezeScreen ? 1 : 0;
        FadeModeCombo.SelectedIndex = (int)_settings.FadeMode;
        EscBehaviorCombo.SelectedIndex = (int)_settings.EscBehavior;
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        UpdateToggleToolHotkeyDisplay();
        UpdateDragStyleLabels();
    }

    private void RefreshSpotlightTabUI()
    {
        OpacitySlider.Value = 100 - _settings.OverlayOpacity * 100;
        OpacityTextBox.Text = ((int)(100 - _settings.OverlayOpacity * 100)).ToString() + "%";
        FeatherSlider.Value = _settings.FeatherRadius;
        FeatherTextBox.Text = _settings.FeatherRadius.ToString();
        PreviewStyleCombo.SelectedIndex = (int)_settings.PreviewStyle;
        SpotlightModeCombo.SelectedIndex = _settings.CumulativeSpotlights ? 0 : 1;
        NestedSpotlightModeCombo.SelectedIndex = (int)_settings.NestedSpotlightMode;
        LoadCustomColors();
        BuildCustomColorSlots();
        HighlightSelectedPreset();
    }

    private void RefreshArrowTabUI()
    {
        ArrowheadStyleCombo_Init();
        InitSizeControls();
        SyncStyleCheck_Init();
        SyncSizeCheck_Init();
        HighlightSelectedPreset();
    }

    private void RefreshBoxTabUI()
    {
        BoxSizeSlider.Value = _settings.BoxLineThickness;
        BoxSizeTextBox.Text = ((int)_settings.BoxLineThickness).ToString();
        BuildBoxColorPresetSwatches();
        BuildBoxCustomColorSlots();
    }

    private void RefreshHighlightTabUI()
    {
        HighlightOpacitySlider.Value = (int)(_settings.HighlightOpacity * 100);
        HighlightOpacityTextBox.Text = ((int)(_settings.HighlightOpacity * 100)).ToString() + "%";
        BuildHighlightColorPresetSwatches();
        BuildHighlightCustomColorSlots();
    }

    private void RefreshStepsTabUI()
    {
        BuildStepsFontFamilyCombo();
        StepsFontSizeTextBox.Text = ((int)_settings.StepsFontSize).ToString();
        StepsSizeSlider.Value = _settings.StepsSize;
        StepsSizeTextBox.Text = ((int)_settings.StepsSize).ToString();
        StepsOutlineEnabledCheck.IsChecked = _settings.StepsOutlineEnabled;
        StepsTailDirectionCombo.SelectedIndex = (int)_settings.StepsTailDirection;
        HighlightStepsShapeToggle();
        HighlightStepsBoldBtn();
        HighlightStepsColorTab();
        BuildStepsFillColorPresetSwatches();
        BuildStepsFillCustomColorSlots();
        BuildStepsOutlineColorPresetSwatches();
        BuildStepsOutlineCustomColorSlots();
        BuildStepsFontColorPresetSwatches();
        BuildStepsFontCustomColorSlots();
    }

    // ── Hotkey recorder ────────────────────────────────────────────

    private static string ModifierDisplayName(Models.ModifierKey mod) => mod switch
    {
        Models.ModifierKey.Alt => "Alt",
        Models.ModifierKey.Shift => "Shift",
        Models.ModifierKey.CtrlShift => "Ctrl + Shift",
        Models.ModifierKey.CtrlAlt => "Ctrl + Alt",
        Models.ModifierKey.None => "",
        _ => "Ctrl"
    };

    private void UpdateHotkeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        var modDisplay = ModifierDisplayName(_settings.ActivationModifier);
        if (!string.IsNullOrEmpty(modDisplay)) parts.Add(modDisplay);
        if (_settings.ActivationKey != 0) parts.Add(VkDisplayName(_settings.ActivationKey));
        if (!IsMouseButtonVk(_settings.ActivationKey)) parts.Add("Click");
        HotkeyDisplay.Text = string.Join(" + ", parts);
        HotkeyHint.Text = "Start using the current tool";
        HotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        HotkeyHint.Visibility = System.Windows.Visibility.Visible;
        HotkeyRecorderBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorder");
    }

    private void HotkeyRecorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isRecordingHotkey) return;
        _isRecordingHotkey = true;
        if (_inputHook != null) _inputHook.IsRecordingHotkey = true;
        _pendingActivationMod = null;
        HotkeyDisplay.Text = "Press modifier(s) + key, or a mouse button...";
        HotkeyHint.Text = "e.g. Ctrl, Ctrl+Space, or Mouse 4";
        HotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        HotkeyHint.Visibility = System.Windows.Visibility.Visible;
        HotkeyRecorderBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
        PreviewKeyDown += HotkeyRecorder_PreviewKeyDown;
        PreviewMouseDown += HotkeyRecorder_PreviewMouseDown;
        HotkeyRecorderBorder.Focusable = true;
        System.Windows.Input.Keyboard.Focus(HotkeyRecorderBorder);
        DebugLog.Write($"[Settings] Activation recording started. KbFocus={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}, IsKbFocusWithin={IsKeyboardFocusWithin}");
    }

    private void StopRecordingActivation()
    {
        _isRecordingHotkey = false;
        if (_inputHook != null) _inputHook.IsRecordingHotkey = false;
        _hotkeyDebounceTimer?.Stop();
        _hotkeyDebounceTimer = null;
        PreviewKeyDown -= HotkeyRecorder_PreviewKeyDown;
        PreviewMouseDown -= HotkeyRecorder_PreviewMouseDown;
    }

    private void HotkeyRecorder_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Only capture middle, X1, X2 — left click is used to start recording
        int vk = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Middle => 0x04,
            System.Windows.Input.MouseButton.XButton1 => 0x05,
            System.Windows.Input.MouseButton.XButton2 => 0x06,
            _ => 0
        };
        if (vk == 0) return;

        e.Handled = true;
        _hotkeyDebounceTimer?.Stop();
        _hotkeyDebounceTimer = null;

        var mod = ReadPhysicalModifiers() ?? _pendingActivationMod ?? Models.ModifierKey.None;

        // Check conflict with toggle
        if (mod == _settings.ToggleModifier && vk == _settings.ToggleKey)
        {
            HotkeyHint.Text = "⚠ Same as Pause app hotkey — pick a different combo";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            HotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        StopRecordingActivation();
        _settings.ActivationModifier = mod;
        _settings.ActivationKey = vk;
        _settings.Save();
        UpdateHotkeyDisplay();
        UpdateDragStyleLabels();
        UpdatePreview();
    }

    private Models.ModifierKey? _pendingActivationMod;

    private void HotkeyRecorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        DebugLog.Write($"[Settings] Activation PreviewKeyDown: key={key}, e.Key={e.Key}, e.SystemKey={e.SystemKey}");

        if (key == System.Windows.Input.Key.Escape)
        {
            DebugLog.Write("[Settings] Escape detected — stopping activation recording");
            StopRecordingActivation();
            UpdateHotkeyDisplay();
            return;
        }

        bool isModifier = key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
            or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift;

        Models.ModifierKey? currentMod = ReadPhysicalModifiers();

        if (isModifier)
        {
            _pendingActivationMod = currentMod;
            if (currentMod != null)
                HotkeyDisplay.Text = ModifierDisplayName(currentMod.Value) + " + Click";

            // Debounce: if user only presses modifiers (no key), finalize after 600ms
            _hotkeyDebounceTimer?.Stop();
            _hotkeyDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            var captured = currentMod;
            _hotkeyDebounceTimer.Tick += (_, _) =>
            {
                _hotkeyDebounceTimer?.Stop();
                _hotkeyDebounceTimer = null;
                if (!_isRecordingHotkey || captured == null) return;

                StopRecordingActivation();
                _settings.ActivationModifier = captured.Value;
                _settings.ActivationKey = 0; // modifier-only
                _settings.Save();
                UpdateHotkeyDisplay();
                UpdateDragStyleLabels();
                ShowHotkeyWarning(captured.Value);
                UpdatePreview();
            };
            _hotkeyDebounceTimer.Start();
            return;
        }

        // Non-modifier key pressed — commit immediately as modifier+key
        _hotkeyDebounceTimer?.Stop();
        _hotkeyDebounceTimer = null;

        var mod = currentMod ?? _pendingActivationMod;
        if (mod == null)
        {
            HotkeyHint.Text = "⚠ Must include at least one modifier (Ctrl, Alt, Shift)";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            HotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        // Check conflict with toggle hotkey
        if (mod.Value == _settings.ToggleModifier && vk == _settings.ToggleKey)
        {
            HotkeyHint.Text = "⚠ Same as Pause app hotkey — pick a different combo";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            HotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        StopRecordingActivation();
        _settings.ActivationModifier = mod.Value;
        _settings.ActivationKey = vk;
        _settings.Save();
        UpdateHotkeyDisplay();
        UpdateDragStyleLabels();
        ShowHotkeyWarning(mod.Value);
        UpdatePreview();
    }

    private System.Windows.Threading.DispatcherTimer? _hotkeyDebounceTimer;

    private void ShowHotkeyWarning(Models.ModifierKey mod)
    {
        // Check for conflict with toggle hotkey
        if (mod == _settings.ToggleModifier && _settings.ActivationKey != 0 
            && _settings.ActivationKey == _settings.ToggleKey)
        {
            HotkeyHint.Text = "⚠ Would conflict with Pause app hotkey";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            HotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        string? warning = mod switch
        {
            Models.ModifierKey.Alt => "Alt+Click is used by some apps (e.g. Photoshop, some window managers)",
            Models.ModifierKey.Shift => "Shift+Click is commonly used for range selection",
            _ => null
        };

        if (warning != null)
        {
            HotkeyHint.Text = "⚠ " + warning;
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            HotkeyHint.Visibility = System.Windows.Visibility.Visible;
        }
    }

    // ── Toggle hotkey recorder ─────────────────────────────────────

    private bool _isRecordingToggleHotkey;
    private Models.ModifierKey? _pendingToggleMod;

    public static string VkDisplayName(int vk)
    {
        // Mouse buttons
        if (vk == 0x01) return "Left Click";
        if (vk == 0x02) return "Right Click";
        if (vk == 0x04) return "Middle Click";
        if (vk == 0x05) return "Mouse 4";
        if (vk == 0x06) return "Mouse 5";
        // Common keys
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString(); // 0-9
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString(); // A-Z
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);    // F1-F24
        return vk switch
        {
            0x20 => "Space", 0x09 => "Tab", 0x0D => "Enter",
            0xBE => ".", 0xBC => ",", 0xBF => "/", 0xBA => ";",
            0xDE => "'", 0xDB => "[", 0xDD => "]", 0xDC => "\\",
            0xBD => "-", 0xBB => "=", 0xC0 => "`",
            _ => $"0x{vk:X2}"
        };
    }

    private static bool IsMouseButtonVk(int vk) => vk is 0x01 or 0x02 or 0x04 or 0x05 or 0x06;

    private void UpdateToggleHotkeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        var modDisplay = ModifierDisplayName(_settings.ToggleModifier);
        if (!string.IsNullOrEmpty(modDisplay)) parts.Add(modDisplay);
        parts.Add(VkDisplayName(_settings.ToggleKey));
        ToggleHotkeyDisplay.Text = string.Join(" + ", parts);
        ToggleHotkeyHint.Text = "Toggle all functionality on/off";
        ToggleHotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        ToggleHotkeyHint.Visibility = System.Windows.Visibility.Visible;
        ToggleHotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorder");
    }

    private void ToggleHotkeyRecorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isRecordingToggleHotkey) return;
        _isRecordingToggleHotkey = true;
        _pendingToggleMod = null;
        if (_inputHook != null) _inputHook.IsRecordingHotkey = true;
        ToggleHotkeyDisplay.Text = "Press modifier(s) + key, or a mouse button...";
        ToggleHotkeyHint.Text = "e.g. Ctrl+Shift+Q, or Mouse 4";
        ToggleHotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        ToggleHotkeyHint.Visibility = System.Windows.Visibility.Visible;
        ToggleHotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
        PreviewKeyDown += ToggleHotkeyRecorder_PreviewKeyDown;
        PreviewMouseDown += ToggleHotkeyRecorder_PreviewMouseDown;
        ToggleHotkeyBorder.Focusable = true;
        System.Windows.Input.Keyboard.Focus(ToggleHotkeyBorder);
    }

    private void StopRecordingToggle()
    {
        _isRecordingToggleHotkey = false;
        _pendingToggleMod = null;
        if (_inputHook != null) _inputHook.IsRecordingHotkey = false;
        PreviewKeyDown -= ToggleHotkeyRecorder_PreviewKeyDown;
        PreviewMouseDown -= ToggleHotkeyRecorder_PreviewMouseDown;
    }

    // ── Toggle Tool hotkey recorder ────────────────────────────────

    private bool _isRecordingToggleToolHotkey;
    private Models.ModifierKey? _pendingToggleToolMod;

    private void UpdateToggleToolHotkeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        var modDisplay = ModifierDisplayName(_settings.ToggleToolModifier);
        if (!string.IsNullOrEmpty(modDisplay)) parts.Add(modDisplay);
        parts.Add(VkDisplayName(_settings.ToggleToolKey));
        ToggleToolHotkeyDisplay.Text = string.Join(" + ", parts);
        ToggleToolHotkeyHint.Text = "Cycle to the next tool";
        ToggleToolHotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
        ToggleToolHotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorder");
    }

    private void ToggleToolHotkeyRecorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isRecordingToggleToolHotkey) return;
        _isRecordingToggleToolHotkey = true;
        _pendingToggleToolMod = null;
        if (_inputHook != null) _inputHook.IsRecordingHotkey = true;
        ToggleToolHotkeyDisplay.Text = "Press modifier(s) + key, or a mouse button...";
        ToggleToolHotkeyHint.Text = "e.g. Ctrl+Shift+Right Click, or Mouse 4";
        ToggleToolHotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
        ToggleToolHotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
        PreviewKeyDown += ToggleToolHotkeyRecorder_PreviewKeyDown;
        PreviewMouseDown += ToggleToolHotkeyRecorder_PreviewMouseDown;
        ToggleToolHotkeyBorder.Focusable = true;
        System.Windows.Input.Keyboard.Focus(ToggleToolHotkeyBorder);
    }

    private void StopRecordingToggleTool()
    {
        _isRecordingToggleToolHotkey = false;
        _pendingToggleToolMod = null;
        if (_inputHook != null) _inputHook.IsRecordingHotkey = false;
        PreviewKeyDown -= ToggleToolHotkeyRecorder_PreviewKeyDown;
        PreviewMouseDown -= ToggleToolHotkeyRecorder_PreviewMouseDown;
    }

    private void ToggleToolHotkeyRecorder_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        int vk = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => 0x01,
            System.Windows.Input.MouseButton.Right => 0x02,
            System.Windows.Input.MouseButton.Middle => 0x04,
            System.Windows.Input.MouseButton.XButton1 => 0x05,
            System.Windows.Input.MouseButton.XButton2 => 0x06,
            _ => 0
        };
        if (vk == 0) return;

        e.Handled = true;
        var mod = ReadPhysicalModifiers() ?? _pendingToggleToolMod ?? Models.ModifierKey.None;

        // Check conflict with activation and toggle
        if (mod == _settings.ActivationModifier && vk == _settings.ActivationKey)
        {
            ToggleToolHotkeyHint.Text = "⚠ Same as Activation hotkey — pick a different combo";
            ToggleToolHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }
        if (mod == _settings.ToggleModifier && vk == _settings.ToggleKey)
        {
            ToggleToolHotkeyHint.Text = "⚠ Same as Pause app hotkey — pick a different combo";
            ToggleToolHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        StopRecordingToggleTool();
        _settings.ToggleToolModifier = mod;
        _settings.ToggleToolKey = vk;
        _settings.Save();
        UpdateToggleToolHotkeyDisplay();
    }

    private void ToggleToolHotkeyRecorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key == System.Windows.Input.Key.Escape)
        {
            StopRecordingToggleTool();
            UpdateToggleToolHotkeyDisplay();
            return;
        }

        bool isModifier = key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
            or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift;

        Models.ModifierKey? currentMod = ReadPhysicalModifiers();

        if (isModifier)
        {
            _pendingToggleToolMod = currentMod;
            if (currentMod != null)
                ToggleToolHotkeyDisplay.Text = ModifierDisplayName(currentMod.Value) + " + ...";
            return;
        }

        var mod = currentMod ?? _pendingToggleToolMod;
        if (mod == null)
        {
            ToggleToolHotkeyHint.Text = "⚠ Must include at least one modifier (Ctrl, Alt, Shift)";
            ToggleToolHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        if (mod.Value == _settings.ActivationModifier && vk == _settings.ActivationKey)
        {
            ToggleToolHotkeyHint.Text = "⚠ Same as Activation hotkey — pick a different combo";
            ToggleToolHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }
        if (mod.Value == _settings.ToggleModifier && vk == _settings.ToggleKey)
        {
            ToggleToolHotkeyHint.Text = "⚠ Same as Pause app hotkey — pick a different combo";
            ToggleToolHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleToolHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        StopRecordingToggleTool();
        _settings.ToggleToolModifier = mod.Value;
        _settings.ToggleToolKey = vk;
        _settings.Save();
        UpdateToggleToolHotkeyDisplay();
    }

    private void ToggleHotkeyRecorder_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        int vk = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Middle => 0x04,
            System.Windows.Input.MouseButton.XButton1 => 0x05,
            System.Windows.Input.MouseButton.XButton2 => 0x06,
            _ => 0
        };
        if (vk == 0) return;

        e.Handled = true;
        var mod = ReadPhysicalModifiers() ?? _pendingToggleMod ?? Models.ModifierKey.None;

        // Check conflict with activation
        if (mod == _settings.ActivationModifier && vk == _settings.ActivationKey)
        {
            ToggleHotkeyHint.Text = "⚠ Same as Activation hotkey — pick a different combo";
            ToggleHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        StopRecordingToggle();
        _settings.ToggleModifier = mod;
        _settings.ToggleKey = vk;
        _settings.Save();
        UpdateToggleHotkeyDisplay();
    }

    private void ToggleHotkeyRecorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        DebugLog.Write($"[Settings] Toggle PreviewKeyDown: key={key}, e.Key={e.Key}, e.SystemKey={e.SystemKey}");

        e.Handled = true;

        if (key == System.Windows.Input.Key.Escape)
        {
            DebugLog.Write("[Settings] Escape detected — stopping toggle recording");
            StopRecordingToggle();
            UpdateToggleHotkeyDisplay();
            return;
        }

        bool isModifier = key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
            or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift;

        Models.ModifierKey? currentMod = ReadPhysicalModifiers();

        if (isModifier)
        {
            _pendingToggleMod = currentMod;
            if (currentMod != null)
                ToggleHotkeyDisplay.Text = ModifierDisplayName(currentMod.Value) + " + ...";
            return;
        }

        // Non-modifier key pressed — need at least one modifier for keyboard keys
        var mod = currentMod ?? _pendingToggleMod;
        if (mod == null)
        {
            ToggleHotkeyHint.Text = "⚠ Must include at least one modifier (Ctrl, Alt, Shift)";
            ToggleHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        // Check conflict with activation hotkey
        if (mod.Value == _settings.ActivationModifier && vk == _settings.ActivationKey)
        {
            ToggleHotkeyHint.Text = "⚠ Same as Activation hotkey — pick a different combo";
            ToggleHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
            ToggleHotkeyHint.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        StopRecordingToggle();
        _settings.ToggleModifier = mod.Value;
        _settings.ToggleKey = vk;
        _settings.Save();
        UpdateToggleHotkeyDisplay();
    }

    // ── Color preset grid ──────────────────────────────────────────

    private void BuildColorPresetSwatches()
    {
        ColorPresetGrid.Children.Clear();
        foreach (var (hex, name) in PresetColors)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14), // circular
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = hex,
                ToolTip = name,
            };
            swatch.MouseLeftButtonDown += ColorSwatch_Click;
            ColorPresetGrid.Children.Add(swatch);
        }
        HighlightSelectedPreset();
    }

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string hex)
        {
            _settings.ArrowColor = hex;
            _settings.Save();
            HighlightSelectedPreset();
            UpdateArrowPreview();
        }
    }

    private void HighlightSelectedPreset()
    {
        var accentBrush = (SolidColorBrush)FindResource("Accent");
        var borderBrush = (SolidColorBrush)FindResource("CardBorder");
        foreach (var grid in new[] { ColorPresetGrid, CustomColorGrid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex)
                {
                    bool isSelected = string.Equals(hex, _settings.ArrowColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderBrush = isSelected ? accentBrush : borderBrush;
                    swatch.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                }
            }
        }
    }

    // ── Custom color slots ─────────────────────────────────────────

    // ── Sync toggles ──────────────────────────────────────────────

    private void SyncStyleCheck_Init()
    {
        SyncStyleCheck.IsChecked = _settings.SyncArrowEndStyle;
        UpdateSyncStyleCheckEnabled();
    }

    private void SyncSizeCheck_Init()
    {
        SyncSizeCheck.IsChecked = _settings.SyncArrowEndSize;
        UpdateSyncSizeCheckEnabled();
    }

    private void SyncStyleCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || !IsLoaded) return;
        _settings.SyncArrowEndStyle = SyncStyleCheck.IsChecked == true;
        _settings.Save();
    }

    private void SyncSizeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || !IsLoaded) return;
        _settings.SyncArrowEndSize = SyncSizeCheck.IsChecked == true;
        _settings.Save();
        HighlightSizeToggle();
    }

    private void UpdateSyncStyleCheckEnabled()
    {
        bool hasTwoEnds = _settings.ArrowheadStyle != ArrowheadStyle.None && _settings.ArrowEndStyle != ArrowheadStyle.None;
        SyncStyleCheck.Visibility = hasTwoEnds ? Visibility.Visible : Visibility.Hidden;
    }

    private void UpdateSyncSizeCheckEnabled()
    {
        bool hasTwoEnds = _settings.ArrowheadStyle != ArrowheadStyle.None && _settings.ArrowEndStyle != ArrowheadStyle.None;
        SyncSizeCheck.Visibility = hasTwoEnds ? Visibility.Visible : Visibility.Hidden;
    }

    // Keep old handlers as no-ops for compatibility
    private void SyncCheck_Changed(object sender, RoutedEventArgs e) { }
    private void SyncStyleBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void SyncSizeBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

    // ── Arrow Preview ──────────────────────────────────────────────

    private static readonly Rendering.ArrowRenderer _previewRenderer = new();

    private void UpdateArrowPreview()
    {
        if (ArrowPreviewArea == null) return;
        ArrowPreviewArea.Children.Clear();

        double w = ArrowPreviewArea.ActualWidth > 0 ? ArrowPreviewArea.ActualWidth : 486;
        double h = ArrowPreviewArea.ActualHeight > 0 ? ArrowPreviewArea.ActualHeight : 80;

        var start = new System.Windows.Point(w * 0.3, h * 0.5);
        var end = new System.Windows.Point(w * 0.7, h * 0.5);

        var color = ParseHexColor(_settings.ArrowColor);
        var leftEnd = _settings.ArrowheadStyle;
        var rightEnd = _settings.ArrowEndStyle;
        var lineStyle = _settings.ArrowLineStyle;

        var shadow = _previewRenderer.BuildShadowPath(start, end, leftEnd, rightEnd, lineStyle,
            _settings.ArrowLeftEndSize, _settings.ArrowLineThickness, _settings.ArrowRightEndSize);
        if (shadow != null) { shadow.IsHitTestVisible = false; ArrowPreviewArea.Children.Add(shadow); }

        var arrow = _previewRenderer.BuildArrowPath(start, end, color, leftEnd, rightEnd, lineStyle,
            _settings.ArrowLeftEndSize, _settings.ArrowLineThickness, _settings.ArrowRightEndSize);
        if (arrow != null) { arrow.IsHitTestVisible = false; ArrowPreviewArea.Children.Add(arrow); }
    }

    private static System.Windows.Media.Color ParseHexColor(string hex)
    {
        try
        {
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return System.Windows.Media.Colors.Red;
    }

    // ── Custom color slots ─────────────────────────────────────────

    private const int MaxCustomColors = 10;
    private readonly List<string> _customColors = new();

    private void LoadCustomColors()
    {
        _customColors.Clear();
        if (!string.IsNullOrEmpty(_settings.CustomColors))
        {
            foreach (var hex in _settings.CustomColors.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (hex.Length == 6 && _customColors.Count < MaxCustomColors)
                    _customColors.Add(hex.Trim());
            }
        }
    }

    private void SaveCustomColors()
    {
        _settings.CustomColors = string.Join(",", _customColors);
        _settings.Save();
    }

    private void BuildCustomColorSlots()
    {
        CustomColorGrid.Children.Clear();
        for (int i = 0; i < MaxCustomColors; i++)
        {
            string? hex = i < _customColors.Count ? _customColors[i] : null;
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(2),
                Background = hex != null
                    ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex))
                    : System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = hex != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = hex,
            };
            if (hex != null)
                swatch.MouseLeftButtonDown += ColorSwatch_Click;
            CustomColorGrid.Children.Add(swatch);
        }
    }

    private void AddCustomColor(string hex)
    {
        _customColors.Remove(hex);
        _customColors.Insert(0, hex);
        if (_customColors.Count > MaxCustomColors)
            _customColors.RemoveAt(MaxCustomColors);
        SaveCustomColors();
        BuildCustomColorSlots();
        HighlightSelectedPreset();
        BuildBoxCustomColorSlots();
        BuildHighlightCustomColorSlots();
        BuildStepsFillCustomColorSlots();
        BuildStepsOutlineCustomColorSlots();
        BuildStepsFontCustomColorSlots();
    }

    // ── Edit Colors dialog ─────────────────────────────────────────

    private void EditColors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_settings.ArrowColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedHex != null)
        {
            _settings.ArrowColor = dialog.SelectedHex;
            _settings.Save();
            AddCustomColor(dialog.SelectedHex);
            HighlightSelectedPreset();
            UpdateArrowPreview();
        }
    }

    // ── Box tab ────────────────────────────────────────────────────

    private static readonly Rendering.BoxRenderer _boxPreviewRenderer = new();

    private void UpdateBoxPreview()
    {
        if (BoxPreviewArea == null) return;
        BoxPreviewArea.Children.Clear();

        double w = BoxPreviewArea.ActualWidth > 0 ? BoxPreviewArea.ActualWidth : 486;
        double h = BoxPreviewArea.ActualHeight > 0 ? BoxPreviewArea.ActualHeight : 80;

        // Match arrow preview span: x from 30% to 70% of width, y from 20% to 80% of height
        var rect = new Rect(w * 0.3, h * 0.2, w * 0.4, h * 0.6);
        var color = ParseHexColor(_settings.BoxColor);

        var shadow = _boxPreviewRenderer.BuildShadowPath(rect, _settings.BoxLineThickness);
        if (shadow != null) { shadow.IsHitTestVisible = false; BoxPreviewArea.Children.Add(shadow); }

        var box = _boxPreviewRenderer.BuildBoxPath(rect, color, _settings.BoxLineThickness);
        if (box != null) { box.IsHitTestVisible = false; BoxPreviewArea.Children.Add(box); }
    }

    private void BoxSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || !IsLoaded) return;
        var val = Math.Clamp(e.NewValue, 1, 12);
        _settings.BoxLineThickness = val;
        _settings.Save();
        if (BoxSizeTextBox != null) BoxSizeTextBox.Text = ((int)val).ToString();
        UpdateBoxPreview();
    }

    private void ApplyBoxSizeFromTextBox()
    {
        if (int.TryParse(BoxSizeTextBox.Text, out int val))
        {
            val = Math.Clamp(val, 1, 12);
            _isInitializing = true;
            BoxSizeSlider.Value = val;
            _isInitializing = false;
            _settings.BoxLineThickness = val;
            _settings.Save();
            BoxSizeTextBox.Text = val.ToString();
            UpdateBoxPreview();
        }
        else
        {
            BoxSizeTextBox.Text = ((int)_settings.BoxLineThickness).ToString();
        }
    }

    private void BoxSizeTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyBoxSizeFromTextBox();
    private void BoxSizeTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplyBoxSizeFromTextBox();
    }

    private void BoxSizeUp_Click(object sender, RoutedEventArgs e)
    {
        BoxSizeSlider.Value = Math.Clamp(BoxSizeSlider.Value + 1, 1, 12);
    }
    private void BoxSizeDown_Click(object sender, RoutedEventArgs e)
    {
        BoxSizeSlider.Value = Math.Clamp(BoxSizeSlider.Value - 1, 1, 12);
    }

    private void BuildBoxColorPresetSwatches()
    {
        BoxColorPresetGrid.Children.Clear();
        foreach (var (hex, name) in PresetColors)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = hex,
                ToolTip = name,
            };
            swatch.MouseLeftButtonDown += BoxColorSwatch_Click;
            BoxColorPresetGrid.Children.Add(swatch);
        }
        HighlightSelectedBoxPreset();
    }

    private void BoxColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string hex)
        {
            _settings.BoxColor = hex;
            _settings.Save();
            HighlightSelectedBoxPreset();
            UpdateBoxPreview();
        }
    }

    private void HighlightSelectedBoxPreset()
    {
        var accentBrush = (SolidColorBrush)FindResource("Accent");
        var borderBrush = (SolidColorBrush)FindResource("CardBorder");
        foreach (var grid in new[] { BoxColorPresetGrid, BoxCustomColorGrid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex)
                {
                    bool isSelected = string.Equals(hex, _settings.BoxColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderBrush = isSelected ? accentBrush : borderBrush;
                    swatch.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                }
            }
        }
    }

    private void BuildBoxCustomColorSlots()
    {
        BoxCustomColorGrid.Children.Clear();
        for (int i = 0; i < MaxCustomColors; i++)
        {
            string? hex = i < _customColors.Count ? _customColors[i] : null;
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(2),
                Background = hex != null
                    ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex))
                    : System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = hex != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = hex,
            };
            if (hex != null)
                swatch.MouseLeftButtonDown += BoxColorSwatch_Click;
            BoxCustomColorGrid.Children.Add(swatch);
        }
        HighlightSelectedBoxPreset();
    }

    private void BoxEditColors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_settings.BoxColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedHex != null)
        {
            _settings.BoxColor = dialog.SelectedHex;
            _settings.Save();
            // Share custom color storage with Arrow tab
            _customColors.Remove(dialog.SelectedHex);
            _customColors.Insert(0, dialog.SelectedHex);
            if (_customColors.Count > MaxCustomColors)
                _customColors.RemoveAt(MaxCustomColors);
            SaveCustomColors();
            BuildCustomColorSlots();
            HighlightSelectedPreset();
            BuildBoxCustomColorSlots();
            HighlightSelectedBoxPreset();
            BuildStepsFillCustomColorSlots();
            BuildStepsOutlineCustomColorSlots();
            UpdateBoxPreview();
        }
    }

    // ── Highlight tab ──────────────────────────────────────────────

    private static readonly Rendering.HighlightRenderer _highlightPreviewRenderer = new();

    private void UpdateHighlightPreview()
    {
        if (HighlightPreviewArea == null) return;
        HighlightPreviewArea.Children.Clear();

        double w = HighlightPreviewArea.ActualWidth > 0 ? HighlightPreviewArea.ActualWidth : 486;
        double h = HighlightPreviewArea.ActualHeight > 0 ? HighlightPreviewArea.ActualHeight : 80;

        var color = ParseHexColor(_settings.HighlightColor);

        // Two side-by-side examples, each occupying half the canvas
        DrawHighlightExample(w * 0.0, w * 0.5, h, "Abcdefghijklm",
            System.Windows.Media.Brushes.White, System.Windows.Media.Brushes.Black, color);
        DrawHighlightExample(w * 0.5, w * 0.5, h, "Nopqrstuvwxyz",
            new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            System.Windows.Media.Brushes.White, color);
    }

    private void DrawHighlightExample(double offsetX, double panelW, double h,
        string text, System.Windows.Media.Brush bgBrush, System.Windows.Media.Brush fgBrush, Color highlightColor)
    {
        // Panel background
        var bg = new System.Windows.Shapes.Rectangle
        {
            Width = panelW, Height = h,
            Fill = bgBrush, IsHitTestVisible = false
        };
        Canvas.SetLeft(bg, offsetX);
        Canvas.SetTop(bg, 0);
        HighlightPreviewArea.Children.Add(bg);

        // Highlight rect: same proportions as box preview (30–70% of panel width, 20–80% of height)
        double hlX = offsetX + panelW * 0.3;
        double hlY = h * 0.2;
        double hlW = panelW * 0.4;
        double hlH = h * 0.6;

        // Text band: 1/3 of highlight height, 5/4 of highlight width, centered on highlight
        double tbH = hlH / 3.0;
        double tbW = hlW * 1.25;
        double tbX = hlX + (hlW - tbW) / 2.0;
        double tbY = hlY + (hlH - tbH) / 2.0;

        var textBg = new System.Windows.Shapes.Rectangle
        {
            Width = tbW, Height = tbH,
            Fill = bgBrush, IsHitTestVisible = false
        };
        Canvas.SetLeft(textBg, tbX);
        Canvas.SetTop(textBg, tbY);
        HighlightPreviewArea.Children.Add(textBg);

        var tb = new TextBlock
        {
            Text = text,
            Foreground = fgBrush,
            FontSize = tbH * 0.75,
            IsHitTestVisible = false,
            Width = tbW,
            TextAlignment = TextAlignment.Center
        };
        tb.Measure(new Size(tbW, tbH));
        Canvas.SetLeft(tb, tbX);
        Canvas.SetTop(tb, tbY + (tbH - tb.DesiredSize.Height) / 2.0);
        HighlightPreviewArea.Children.Add(tb);

        // Highlight fill on top
        var hlRect = new Rect(hlX, hlY, hlW, hlH);
        var fill = _highlightPreviewRenderer.BuildHighlightPath(hlRect, highlightColor);
        if (fill != null)
        {
            fill.IsHitTestVisible = false;
            fill.Opacity = _settings.HighlightOpacity;
            HighlightPreviewArea.Children.Add(fill);
        }
    }

    private void HighlightOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || !IsLoaded) return;
        var pct = (int)Math.Clamp(e.NewValue, 10, 90);
        _settings.HighlightOpacity = pct / 100.0;
        _settings.Save();
        if (HighlightOpacityTextBox != null) HighlightOpacityTextBox.Text = pct.ToString() + "%";
        UpdateHighlightPreview();
    }

    private void ApplyHighlightOpacityFromTextBox()
    {
        var text = HighlightOpacityTextBox.Text.TrimEnd('%', ' ');
        if (int.TryParse(text, out int pct))
        {
            pct = Math.Clamp(pct, 10, 90);
            _isInitializing = true;
            HighlightOpacitySlider.Value = pct;
            _isInitializing = false;
            _settings.HighlightOpacity = pct / 100.0;
            _settings.Save();
            HighlightOpacityTextBox.Text = pct.ToString() + "%";
            UpdateHighlightPreview();
        }
        else
        {
            HighlightOpacityTextBox.Text = ((int)(_settings.HighlightOpacity * 100)).ToString() + "%";
        }
    }

    private void HighlightOpacityTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHighlightOpacityFromTextBox();
    private void HighlightOpacityTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplyHighlightOpacityFromTextBox();
    }

    private void HighlightOpacityUp_Click(object sender, RoutedEventArgs e)
    {
        HighlightOpacitySlider.Value = Math.Clamp(HighlightOpacitySlider.Value + 10, 10, 90);
    }
    private void HighlightOpacityDown_Click(object sender, RoutedEventArgs e)
    {
        HighlightOpacitySlider.Value = Math.Clamp(HighlightOpacitySlider.Value - 10, 10, 90);
    }

    private void BuildHighlightColorPresetSwatches()
    {
        HighlightColorPresetGrid.Children.Clear();
        foreach (var (hex, name) in PresetColors)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = hex,
                ToolTip = name,
            };
            swatch.MouseLeftButtonDown += HighlightColorSwatch_Click;
            HighlightColorPresetGrid.Children.Add(swatch);
        }
        HighlightSelectedHighlightPreset();
    }

    private void HighlightColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string hex)
        {
            _settings.HighlightColor = hex;
            _settings.Save();
            HighlightSelectedHighlightPreset();
            UpdateHighlightPreview();
        }
    }

    private void HighlightSelectedHighlightPreset()
    {
        var accentBrush = (SolidColorBrush)FindResource("Accent");
        var borderBrush = (SolidColorBrush)FindResource("CardBorder");
        foreach (var grid in new[] { HighlightColorPresetGrid, HighlightCustomColorGrid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex)
                {
                    bool isSelected = string.Equals(hex, _settings.HighlightColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderBrush = isSelected ? accentBrush : borderBrush;
                    swatch.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                }
            }
        }
    }

    private void BuildHighlightCustomColorSlots()
    {
        HighlightCustomColorGrid.Children.Clear();
        for (int i = 0; i < MaxCustomColors; i++)
        {
            string? hex = i < _customColors.Count ? _customColors[i] : null;
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(2),
                Background = hex != null
                    ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex))
                    : System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = hex != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = hex,
            };
            if (hex != null)
                swatch.MouseLeftButtonDown += HighlightColorSwatch_Click;
            HighlightCustomColorGrid.Children.Add(swatch);
        }
        HighlightSelectedHighlightPreset();
    }

    private void HighlightEditColors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_settings.HighlightColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedHex != null)
        {
            _settings.HighlightColor = dialog.SelectedHex;
            _settings.Save();
            _customColors.Remove(dialog.SelectedHex);
            _customColors.Insert(0, dialog.SelectedHex);
            if (_customColors.Count > MaxCustomColors)
                _customColors.RemoveAt(MaxCustomColors);
            SaveCustomColors();
            BuildCustomColorSlots();
            HighlightSelectedPreset();
            BuildBoxCustomColorSlots();
            HighlightSelectedBoxPreset();
            BuildHighlightCustomColorSlots();
            HighlightSelectedHighlightPreset();
            BuildStepsFillCustomColorSlots();
            BuildStepsOutlineCustomColorSlots();
            UpdateHighlightPreview();
        }
    }

    // ── Steps tab ──────────────────────────────────────────────────

    private static readonly Rendering.StepsRenderer _stepsPreviewRenderer = new();

    private void UpdateStepsPreview()
    {
        if (StepsPreviewArea == null) return;
        StepsPreviewArea.Children.Clear();

        double w = StepsPreviewArea.ActualWidth > 0 ? StepsPreviewArea.ActualWidth : 486;
        double h = StepsPreviewArea.ActualHeight > 0 ? StepsPreviewArea.ActualHeight : 80;

        var fillColor    = ParseHexColor(_settings.StepsFillColor);
        var outlineColor = ParseHexColor(_settings.StepsOutlineColor);
        var fontColor    = ParseHexColor(_settings.StepsFontColor);
        var options = new Rendering.StepsRenderOptions(
            _settings.StepsShape,
            _settings.StepsOutlineEnabled,
            _settings.StepsSize,
            fillColor,
            outlineColor,
            _settings.StepsFontFamily,
            _settings.StepsFontSize,
            _settings.StepsFontBold,
            fontColor);

        // Show steps 1-4 pointed left, up, down, right — evenly spaced
        double[] angles = { Math.PI, -Math.PI / 2, Math.PI / 2, 0 };
        double spacing = w / 5.0;
        for (int i = 0; i < 4; i++)
        {
            var anchorDip = new System.Windows.Point(spacing * (i + 1), h / 2);
            var visual = _stepsPreviewRenderer.BuildStepVisual(anchorDip, angles[i], i + 1, options);
            visual.IsHitTestVisible = false;
            StepsPreviewArea.Children.Add(visual);
        }
    }

    private static readonly HashSet<string> _excludedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Symbol-only fonts
        "Bookshelf Symbol 7", "Marlett", "MS Outlook", "MS Reference Specialty",
        "MT Extra", "Segoe Fluent Icons", "Segoe MDL2 Assets", "Webdings",
        "Wingdings", "Wingdings 2", "Wingdings 3",
        // Duplicates / variants
        "Cambria", "Cascadia Mono", "Microsoft PhagsPa", "Microsoft YaHei",
        "MingLiU_HKSCS-ExtB", "MingLiU_MSCS_ExtB", "Niagara Engraved",
        "Nirmala Text", "Segoe Print", "Segoe UI Emoji", "Segoe UI Historic",
        "Segoe UI Symbol", "Segoe UI Variable Display", "Segoe UI Variable Small",
        "Segoe UI Variable Text", "SimSun-ExtB", "SimSun-ExtG",
        "Sitka Display", "Sitka Heading", "Sitka Subheading", "Sitka Text"
    };

    private void BuildStepsFontFamilyCombo()
    {
        _isInitializing = true;
        StepsFontFamilyCombo.Items.Clear();

        // Get all system font families, sorted by name, excluding symbol/duplicate fonts
        var fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(n => !_excludedFonts.Contains(n))
            .OrderBy(n => n)
            .ToList();

        int selectedIndex = 0;
        for (int i = 0; i < fonts.Count; i++)
        {
            var fontName = fonts[i];
            if (string.Equals(fontName, _settings.StepsFontFamily, StringComparison.OrdinalIgnoreCase))
                selectedIndex = i;

            // Each item: "1234567890 - FontName" rendered in that font
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = $"1234567890",
                FontFamily = new System.Windows.Media.FontFamily(fontName),
                FontSize = 13,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = $" — {fontName}",
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Tag = fontName;

            StepsFontFamilyCombo.Items.Add(panel);
        }

        StepsFontFamilyCombo.SelectedIndex = selectedIndex;
        _isInitializing = false;
    }

    private void StepsFontFamilyCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || !IsLoaded) return;
        if (StepsFontFamilyCombo.SelectedItem is StackPanel panel && panel.Tag is string fontName)
        {
            _settings.StepsFontFamily = fontName;
            _settings.Save();
            UpdateStepsPreview();
        }
    }

    private void ApplyStepsFontSizeFromTextBox()
    {
        if (int.TryParse(StepsFontSizeTextBox.Text, out int val))
        {
            val = Math.Clamp(val, 8, 48);
            _settings.StepsFontSize = val;
            _settings.Save();
            StepsFontSizeTextBox.Text = val.ToString();
            UpdateStepsPreview();
        }
        else { StepsFontSizeTextBox.Text = ((int)_settings.StepsFontSize).ToString(); }
    }

    private void StepsFontSizeTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyStepsFontSizeFromTextBox();
    private void StepsFontSizeTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplyStepsFontSizeFromTextBox();
    }

    private void StepsFontSizeUp_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp((int)_settings.StepsFontSize + 1, 8, 48);
        _settings.StepsFontSize = val;
        _settings.Save();
        StepsFontSizeTextBox.Text = val.ToString();
        UpdateStepsPreview();
    }

    private void StepsFontSizeDown_Click(object sender, RoutedEventArgs e)
    {
        var val = Math.Clamp((int)_settings.StepsFontSize - 1, 8, 48);
        _settings.StepsFontSize = val;
        _settings.Save();
        StepsFontSizeTextBox.Text = val.ToString();
        UpdateStepsPreview();
    }

    private void StepsShapeToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border btn && btn.Tag is string tag)
        {
            _settings.StepsShape = tag == "Circle" ? Models.StepsShape.Circle : Models.StepsShape.Teardrop;
            _settings.Save();
            HighlightStepsShapeToggle();
            UpdateStepsPreview();
        }
    }

    private void HighlightStepsShapeToggle()
    {
        var accent = (SolidColorBrush)FindResource("Accent");
        var normal = (SolidColorBrush)FindResource("ControlBg");
        var borderNormal = (SolidColorBrush)FindResource("CardBorder");
        bool isTeardrop = _settings.StepsShape == Models.StepsShape.Teardrop;
        StepsShapeTeardropBtn.Background = isTeardrop ? accent : normal;
        StepsShapeTeardropBtn.BorderBrush = isTeardrop ? accent : borderNormal;
        StepsShapeCircleBtn.Background = !isTeardrop ? accent : normal;
        StepsShapeCircleBtn.BorderBrush = !isTeardrop ? accent : borderNormal;
    }

    private void StepsBoldBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isInitializing) return;
        _settings.StepsFontBold = !_settings.StepsFontBold;
        _settings.Save();
        HighlightStepsBoldBtn();
        UpdateStepsPreview();
    }

    private void HighlightStepsBoldBtn()
    {
        var accent = (SolidColorBrush)FindResource("Accent");
        var normal = (SolidColorBrush)FindResource("ControlBg");
        var borderNormal = (SolidColorBrush)FindResource("CardBorder");
        StepsBoldBtn.Background = _settings.StepsFontBold ? accent : normal;
        StepsBoldBtn.BorderBrush = _settings.StepsFontBold ? accent : borderNormal;
    }

    // "Fill" | "Outline" | "Font"
    private string _stepsColorTab = "Fill";

    private void StepsColorTab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border btn && btn.Tag is string tag)
        {
            _stepsColorTab = tag;
            HighlightStepsColorTab();
        }
    }

    private void HighlightStepsColorTab()
    {
        var accent = (SolidColorBrush)FindResource("Accent");
        var normal = (SolidColorBrush)FindResource("ControlBg");
        var borderNormal = (SolidColorBrush)FindResource("CardBorder");

        StepsColorFillBtn.Background    = _stepsColorTab == "Fill"    ? accent : normal;
        StepsColorFillBtn.BorderBrush   = _stepsColorTab == "Fill"    ? accent : borderNormal;
        StepsColorOutlineBtn.Background = _stepsColorTab == "Outline" ? accent : normal;
        StepsColorOutlineBtn.BorderBrush= _stepsColorTab == "Outline" ? accent : borderNormal;
        StepsColorFontBtn.Background    = _stepsColorTab == "Font"    ? accent : normal;
        StepsColorFontBtn.BorderBrush   = _stepsColorTab == "Font"    ? accent : borderNormal;

        StepsFillColorPanel.Visibility    = _stepsColorTab == "Fill"    ? Visibility.Visible : Visibility.Collapsed;
        StepsOutlineColorPanel.Visibility = _stepsColorTab == "Outline" ? Visibility.Visible : Visibility.Collapsed;
        StepsFontColorPanel.Visibility    = _stepsColorTab == "Font"    ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StepsOutlineEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.StepsOutlineEnabled = StepsOutlineEnabledCheck.IsChecked == true;
        _settings.Save();
        UpdateStepsPreview();
    }

    private void StepsTailDirectionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.StepsTailDirection = (Models.StepsTailDirection)StepsTailDirectionCombo.SelectedIndex;
        _settings.Save();
    }

    private void StepsSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || !IsLoaded) return;
        var val = Math.Clamp(e.NewValue, 16, 72);
        _settings.StepsSize = val;
        _settings.Save();
        if (StepsSizeTextBox != null) StepsSizeTextBox.Text = ((int)val).ToString();
        UpdateStepsPreview();
    }

    private void ApplyStepsSizeFromTextBox()
    {
        if (int.TryParse(StepsSizeTextBox.Text, out int val))
        {
            val = Math.Clamp(val, 16, 72);
            _isInitializing = true;
            StepsSizeSlider.Value = val;
            _isInitializing = false;
            _settings.StepsSize = val;
            _settings.Save();
            StepsSizeTextBox.Text = val.ToString();
            UpdateStepsPreview();
        }
        else { StepsSizeTextBox.Text = ((int)_settings.StepsSize).ToString(); }
    }

    private void StepsSizeTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyStepsSizeFromTextBox();
    private void StepsSizeTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ApplyStepsSizeFromTextBox();
    }

    private void StepsSizeUp_Click(object sender, RoutedEventArgs e)
    {
        StepsSizeSlider.Value = Math.Clamp(StepsSizeSlider.Value + 1, 16, 72);
    }

    private void StepsSizeDown_Click(object sender, RoutedEventArgs e)
    {
        StepsSizeSlider.Value = Math.Clamp(StepsSizeSlider.Value - 1, 16, 72);
    }

    private void BuildStepsFillColorPresetSwatches()
    {
        StepsFillColorPresetGrid.Children.Clear();
        foreach (var (hex, name) in PresetColors)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(2),
                Background = new SolidColorBrush(color), BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = hex, ToolTip = name,
            };
            swatch.MouseLeftButtonDown += StepsFillColorSwatch_Click;
            StepsFillColorPresetGrid.Children.Add(swatch);
        }
        HighlightSelectedStepsFillPreset();
    }

    private void StepsFillColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string hex)
        {
            _settings.StepsFillColor = hex;
            _settings.Save();
            HighlightSelectedStepsFillPreset();
            UpdateStepsPreview();
        }
    }

    private void HighlightSelectedStepsFillPreset()
    {
        var accentBrush = (SolidColorBrush)FindResource("Accent");
        var borderBrush = (SolidColorBrush)FindResource("CardBorder");
        foreach (var grid in new[] { StepsFillColorPresetGrid, StepsFillCustomColorGrid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex)
                {
                    bool isSelected = string.Equals(hex, _settings.StepsFillColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderBrush = isSelected ? accentBrush : borderBrush;
                    swatch.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                }
            }
        }
    }

    private void BuildStepsFillCustomColorSlots()
    {
        StepsFillCustomColorGrid.Children.Clear();
        for (int i = 0; i < MaxCustomColors; i++)
        {
            string? hex = i < _customColors.Count ? _customColors[i] : null;
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(2),
                Background = hex != null ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex)) : System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1), BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = hex != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow, Tag = hex,
            };
            if (hex != null) swatch.MouseLeftButtonDown += StepsFillColorSwatch_Click;
            StepsFillCustomColorGrid.Children.Add(swatch);
        }
        HighlightSelectedStepsFillPreset();
    }

    private void StepsFillEditColors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_settings.StepsFillColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedHex != null)
        {
            _settings.StepsFillColor = dialog.SelectedHex;
            _settings.Save();
            AddCustomColor(dialog.SelectedHex);
            BuildStepsFillCustomColorSlots();
            HighlightSelectedStepsFillPreset();
            UpdateStepsPreview();
        }
    }

    private void BuildStepsOutlineColorPresetSwatches()
    {
        StepsOutlineColorPresetGrid.Children.Clear();
        foreach (var (hex, name) in PresetColors)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(2),
                Background = new SolidColorBrush(color), BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = hex, ToolTip = name,
            };
            swatch.MouseLeftButtonDown += StepsOutlineColorSwatch_Click;
            StepsOutlineColorPresetGrid.Children.Add(swatch);
        }
        HighlightSelectedStepsOutlinePreset();
    }

    private void StepsOutlineColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string hex)
        {
            _settings.StepsOutlineColor = hex;
            _settings.Save();
            HighlightSelectedStepsOutlinePreset();
            UpdateStepsPreview();
        }
    }

    private void HighlightSelectedStepsOutlinePreset()
    {
        var accentBrush = (SolidColorBrush)FindResource("Accent");
        var borderBrush = (SolidColorBrush)FindResource("CardBorder");
        foreach (var grid in new[] { StepsOutlineColorPresetGrid, StepsOutlineCustomColorGrid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex)
                {
                    bool isSelected = string.Equals(hex, _settings.StepsOutlineColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderBrush = isSelected ? accentBrush : borderBrush;
                    swatch.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                }
            }
        }
    }

    private void BuildStepsOutlineCustomColorSlots()
    {
        StepsOutlineCustomColorGrid.Children.Clear();
        for (int i = 0; i < MaxCustomColors; i++)
        {
            string? hex = i < _customColors.Count ? _customColors[i] : null;
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(2),
                Background = hex != null ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex)) : System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1), BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = hex != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow, Tag = hex,
            };
            if (hex != null) swatch.MouseLeftButtonDown += StepsOutlineColorSwatch_Click;
            StepsOutlineCustomColorGrid.Children.Add(swatch);
        }
        HighlightSelectedStepsOutlinePreset();
    }

    private void StepsOutlineEditColors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_settings.StepsOutlineColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedHex != null)
        {
            _settings.StepsOutlineColor = dialog.SelectedHex;
            _settings.Save();
            AddCustomColor(dialog.SelectedHex);
            BuildStepsOutlineCustomColorSlots();
            HighlightSelectedStepsOutlinePreset();
            UpdateStepsPreview();
        }
    }

    private void BuildStepsFontColorPresetSwatches()
    {
        StepsFontColorPresetGrid.Children.Clear();
        foreach (var (hex, name) in PresetColors)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(2),
                Background = new SolidColorBrush(color), BorderThickness = new Thickness(1),
                BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = hex, ToolTip = name,
            };
            swatch.MouseLeftButtonDown += StepsFontColorSwatch_Click;
            StepsFontColorPresetGrid.Children.Add(swatch);
        }
        HighlightSelectedStepsFontPreset();
    }

    private void StepsFontColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string hex)
        {
            _settings.StepsFontColor = hex;
            _settings.Save();
            HighlightSelectedStepsFontPreset();
            UpdateStepsPreview();
        }
    }

    private void HighlightSelectedStepsFontPreset()
    {
        var accentBrush = (SolidColorBrush)FindResource("Accent");
        var borderBrush = (SolidColorBrush)FindResource("CardBorder");
        foreach (var grid in new[] { StepsFontColorPresetGrid, StepsFontCustomColorGrid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex)
                {
                    bool isSelected = string.Equals(hex, _settings.StepsFontColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderBrush = isSelected ? accentBrush : borderBrush;
                    swatch.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                }
            }
        }
    }

    private void BuildStepsFontCustomColorSlots()
    {
        StepsFontCustomColorGrid.Children.Clear();
        for (int i = 0; i < MaxCustomColors; i++)
        {
            string? hex = i < _customColors.Count ? _customColors[i] : null;
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(2),
                Background = hex != null ? new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex)) : System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1), BorderBrush = (SolidColorBrush)FindResource("CardBorder"),
                Cursor = hex != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow, Tag = hex,
            };
            if (hex != null) swatch.MouseLeftButtonDown += StepsFontColorSwatch_Click;
            StepsFontCustomColorGrid.Children.Add(swatch);
        }
        HighlightSelectedStepsFontPreset();
    }

    private void StepsFontEditColors_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_settings.StepsFontColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedHex != null)
        {
            _settings.StepsFontColor = dialog.SelectedHex;
            _settings.Save();
            AddCustomColor(dialog.SelectedHex);
            BuildStepsFontCustomColorSlots();
            HighlightSelectedStepsFontPreset();
            UpdateStepsPreview();
        }
    }

    // ── HSV ↔ RGB conversion (public static for dialog reuse) ─────

    public static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        double r, g, b;
        if (h < 60)       { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else               { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) RgbToHsv(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r)      h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else               h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max > 0 ? delta / max : 0;
        return (h, s, max);
    }

    // ── Toolbar Tab ────────────────────────────────────────────────

    private void InitToolbarTab()
    {
        ShowToolbarCheck.IsChecked = _settings.FlyoutToolbarVisible;
        ShowToolNameCheck.IsChecked = _settings.ShowToolNameOnSwitch;
        RebuildToolLists();
    }

    private static string ToolLabel(ToolType t) => t switch
    {
        ToolType.Spotlight => "Spotlight",
        ToolType.Arrow     => "Arrow",
        ToolType.Box       => "Box",
        ToolType.Highlight => "Highlighter",
        ToolType.Steps     => "Steps",
        _                  => t.ToString()
    };

    private const string SettingsTag = "Settings";

    private static UIElement BuildToolIcon(ToolType tool)
    {
        const double size = 18;
        switch (tool)
        {
            case ToolType.Spotlight:
            {
                var img = new System.Windows.Controls.Image
                {
                    Width = size, Height = size,
                    Source = FlyoutToolbarWindow.BuildSpotlightIconBitmap(16, 13, featherRadius: 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                return img;
            }
            case ToolType.Arrow:
                return new TextBlock
                {
                    Text = "\u279C", FontSize = size,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
            case ToolType.Box:
            {
                var vb = new System.Windows.Controls.Viewbox { Width = size, Height = size };
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 16, Height = 13,
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1.5,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
                vb.Child = rect;
                return vb;
            }
            case ToolType.Highlight:
                return new TextBlock
                {
                    Text = "\U0001F58D", FontSize = size,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
            case ToolType.Steps:
            {
                var vb = new System.Windows.Controls.Viewbox { Width = size, Height = size };
                var grid = new Grid { Width = 18, Height = 26 };
                var path = new System.Windows.Shapes.Path
                {
                    Fill = System.Windows.Media.Brushes.White,
                    Stroke = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse(
                        "M 2,8 A 7,7 0 1 1 16,8 Q 16,13.1 9,23.4 Q 2,13.1 2,8 Z")
                };
                var num = new TextBlock
                {
                    Text = "1", FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                    IsHitTestVisible = false
                };
                grid.Children.Add(path);
                grid.Children.Add(num);
                vb.Child = grid;
                return vb;
            }
            default:
                return new TextBlock { Width = size };
        }
    }

    private static UIElement BuildSettingsIcon()
    {
        const double size = 18;
        var vb = new System.Windows.Controls.Viewbox { Width = size, Height = size };

        // Build PathGeometry directly so we can set FillRule (Geometry.Parse returns StreamGeometry)
        var outerFigures =
            "M 12,15.5 C 10.07,15.5 8.5,13.93 8.5,12 C 8.5,10.07 10.07,8.5 12,8.5 C 13.93,8.5 15.5,10.07 15.5,12 C 15.5,13.93 13.93,15.5 12,15.5 Z " +
            "M 19.43,12.97 C 19.47,12.65 19.5,12.33 19.5,12 C 19.5,11.67 19.47,11.34 19.43,11.03 L 21.54,9.37 C 21.73,9.22 21.78,8.95 21.66,8.73 L 19.66,5.27 C 19.54,5.05 19.27,4.97 19.05,5.05 L 16.56,6.05 C 16.04,5.65 15.48,5.32 14.87,5.07 L 14.49,2.42 C 14.46,2.18 14.25,2 14,2 L 10,2 C 9.75,2 9.54,2.18 9.51,2.42 L 9.13,5.07 C 8.52,5.32 7.96,5.66 7.44,6.05 L 4.95,5.05 C 4.72,4.96 4.46,5.05 4.34,5.27 L 2.34,8.73 C 2.21,8.95 2.27,9.22 2.46,9.37 L 4.57,11.03 C 4.53,11.34 4.5,11.67 4.5,12 C 4.5,12.33 4.53,12.65 4.57,12.97 L 2.46,14.63 C 2.27,14.78 2.21,15.05 2.34,15.27 L 4.34,18.73 C 4.46,18.95 4.73,19.03 4.95,18.95 L 7.44,17.95 C 7.96,18.35 8.52,18.68 9.13,18.93 L 9.51,21.58 C 9.54,21.82 9.75,22 10,22 L 14,22 C 14.25,22 14.46,21.82 14.49,21.58 L 14.87,18.93 C 15.48,18.68 16.04,18.34 16.56,17.95 L 19.05,18.95 C 19.28,19.04 19.54,18.95 19.66,18.73 L 21.66,15.27 C 21.78,15.05 21.73,14.78 21.54,14.63 Z";

        var pg = System.Windows.Media.PathGeometry.CreateFromGeometry(
            System.Windows.Media.Geometry.Parse(outerFigures));
        pg.FillRule = System.Windows.Media.FillRule.EvenOdd;

        var path = new System.Windows.Shapes.Path
        {
            Fill = System.Windows.Media.Brushes.White,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Data = pg
        };
        vb.Child = path;
        return vb;
    }

    private void RebuildToolLists()
    {
        var allTools = new[] { ToolType.Spotlight, ToolType.Arrow, ToolType.Box, ToolType.Highlight, ToolType.Steps };
        var active = SettingsService.ParseActiveToolOrder(_settings.ToolOrder);
        var available = allTools.Where(t => !active.Contains(t)).ToList();
        bool settingsPresent = _settings.ToolOrder.Contains("Settings", StringComparison.OrdinalIgnoreCase);
        bool settingsAtTop = _settings.ToolOrder.StartsWith("Settings", StringComparison.OrdinalIgnoreCase);

        ActiveToolsList.Children.Clear();
        AvailableToolsList.Children.Clear();

        if (settingsPresent && settingsAtTop)
            ActiveToolsList.Children.Add(BuildSettingsRow());

        foreach (var tool in active)
            ActiveToolsList.Children.Add(BuildToolRow(tool, isActive: true));

        if (settingsPresent && !settingsAtTop)
            ActiveToolsList.Children.Add(BuildSettingsRow());

        // Available section
        bool anyAvailable = available.Count > 0 || !settingsPresent;
        if (!anyAvailable)
        {
            AvailableToolsList.Children.Add(new TextBlock
            {
                Text = "All tools are currently included in the toolbar",
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                FontSize = 11,
                Margin = new Thickness(4, 6, 4, 6),
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var tool in available)
                AvailableToolsList.Children.Add(BuildToolRow(tool, isActive: false));

            if (!settingsPresent)
                AvailableToolsList.Children.Add(BuildAvailableSettingsRow());
        }
    }

    private UIElement BuildAvailableSettingsRow()
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 5, 6, 5),
            Tag = SettingsTag
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconContainer = new Border { Width = 24, Child = BuildSettingsIcon(), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconContainer, 0);
        grid.Children.Add(iconContainer);

        var label = new TextBlock
        {
            Text = "Settings",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var addBtn = BuildIconButton(isRemove: false, tag: SettingsTag);
        Grid.SetColumn(addBtn, 2);
        grid.Children.Add(addBtn);

        row.Child = grid;
        return row;
    }

    private UIElement BuildSettingsRow()
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 5, 6, 5),
            Cursor = System.Windows.Input.Cursors.SizeAll,
            Tag = SettingsTag
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconContainer = new Border { Width = 24, Child = BuildSettingsIcon(), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconContainer, 0);
        grid.Children.Add(iconContainer);

        var label = new TextBlock
        {
            Text = "Settings",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var removeBtn = BuildIconButton(isRemove: true, tag: SettingsTag);
        Grid.SetColumn(removeBtn, 2);
        grid.Children.Add(removeBtn);

        row.Child = grid;
        row.MouseLeftButtonDown += ToolRow_MouseLeftButtonDown;
        row.MouseMove += ToolRow_MouseMove;
        row.MouseLeftButtonUp += ToolRow_MouseLeftButtonUp;
        row.MouseEnter += ToolRow_MouseEnter;
        return row;
    }

    private UIElement BuildToolRow(ToolType tool, bool isActive)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 5, 6, 5),
            Cursor = System.Windows.Input.Cursors.SizeAll,
            Tag = tool
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // button

        var icon = BuildToolIcon(tool);
        var iconContainer = new Border { Width = 24, Child = icon, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconContainer, 0);
        grid.Children.Add(iconContainer);

        var label = new TextBlock
        {
            Text = ToolLabel(tool),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var iconBtn = BuildIconButton(isActive, tool);
        Grid.SetColumn(iconBtn, 2);
        grid.Children.Add(iconBtn);

        row.Child = grid;

        if (isActive)
        {
            row.MouseLeftButtonDown += ToolRow_MouseLeftButtonDown;
            row.MouseMove += ToolRow_MouseMove;
            row.MouseLeftButtonUp += ToolRow_MouseLeftButtonUp;
            row.MouseEnter += ToolRow_MouseEnter;
        }

        return row;
    }

    private UIElement BuildIconButton(bool isRemove, object tag)
    {
        // Transparent by default, colored background only on hover
        var transparentBg = System.Windows.Media.Brushes.Transparent;
        var hoverBg = isRemove
            ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0x9E));

        var border = new Border
        {
            Width = 24, Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = transparentBg,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = tag
        };

        var icon = new TextBlock
        {
            Text = isRemove ? "×" : "+",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = isRemove
                ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
                : new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xD5)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        border.Child = icon;

        border.MouseEnter += (_, _) =>
        {
            border.Background = hoverBg;
            icon.Foreground = System.Windows.Media.Brushes.White;
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = transparentBg;
            icon.Foreground = isRemove
                ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
                : new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xD5));
        };

        // Use PreviewMouseLeftButtonDown to intercept before the row's drag handler
        border.PreviewMouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true; // stop drag from starting
            if (isRemove) ToolRemove_Click(s, new RoutedEventArgs());
            else ToolAdd_Click(s, new RoutedEventArgs());
        };

        return border;
    }

    private void ToolRemove_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag;

        if (tag is string s && s == SettingsTag)
        {
            // Remove Settings button from toolbar
            _settings.ToolOrder = _settings.ToolOrder
                .Replace("Settings,", "", StringComparison.OrdinalIgnoreCase)
                .Replace(",Settings", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Settings", "", StringComparison.OrdinalIgnoreCase);
            _settings.Save();
            RebuildToolLists();
            return;
        }

        if (tag is ToolType tool)
        {
            var order = SettingsService.ParseActiveToolOrder(_settings.ToolOrder);
            if (order.Count <= 1) return;
            order.Remove(tool);
            bool settingsAtTop = _settings.ToolOrder.StartsWith("Settings", StringComparison.OrdinalIgnoreCase);
            bool settingsPresent = _settings.ToolOrder.Contains("Settings", StringComparison.OrdinalIgnoreCase);
            string prefix = settingsPresent && settingsAtTop ? "Settings," : "";
            string suffix = settingsPresent && !settingsAtTop ? ",Settings" : "";
            _settings.ToolOrder = prefix + SettingsService.SerializeToolOrder(order) + suffix;
            _settings.Save();
            RebuildToolLists();
        }
    }

    private void ToolAdd_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag;

        if (tag is string s && s == SettingsTag)
        {
            // Add Settings back at bottom
            var toolPart = SettingsService.SerializeToolOrder(SettingsService.ParseActiveToolOrder(_settings.ToolOrder));
            _settings.ToolOrder = toolPart + ",Settings";
            _settings.Save();
            RebuildToolLists();
            return;
        }

        if (tag is ToolType tool)
        {
            var order = SettingsService.ParseActiveToolOrder(_settings.ToolOrder);
            if (!order.Contains(tool)) order.Add(tool);
            bool settingsAtTop = _settings.ToolOrder.StartsWith("Settings", StringComparison.OrdinalIgnoreCase);
            bool settingsPresent = _settings.ToolOrder.Contains("Settings", StringComparison.OrdinalIgnoreCase);
            string prefix = settingsPresent && settingsAtTop ? "Settings," : "";
            string suffix = settingsPresent && !settingsAtTop ? ",Settings" : "";
            _settings.ToolOrder = prefix + SettingsService.SerializeToolOrder(order) + suffix;
            _settings.Save();
            RebuildToolLists();
        }
    }

    // ── Drag-to-reorder ────────────────────────────────────────────

    private Border? _dragRow;
    private int _dragSourceIndex;
    private bool _isDraggingToolRow;
    private WPoint _toolDragStart;

    private void ToolRow_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border row)
        {
            _dragRow = row;
            _dragSourceIndex = ActiveToolsList.Children.IndexOf(row);
            _toolDragStart = e.GetPosition(ActiveToolsList);
            _isDraggingToolRow = false;
            row.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ToolRow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragRow == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

        var pos = e.GetPosition(ActiveToolsList);
        if (!_isDraggingToolRow)
        {
            if (Math.Abs(pos.Y - _toolDragStart.Y) < 4) return;
            _isDraggingToolRow = true;
        }

        bool isSettingsRow = _dragRow.Tag is string st && st == SettingsTag;

        if (isSettingsRow)
        {
            // Settings snaps only to first or last — determine by which half cursor is in
            double totalHeight = ActiveToolsList.ActualHeight;
            bool goTop = pos.Y < totalHeight / 2;
            int currentIndex = ActiveToolsList.Children.IndexOf(_dragRow);
            int targetIndex = goTop ? 0 : ActiveToolsList.Children.Count - 1;
            if (targetIndex != currentIndex)
            {
                ActiveToolsList.Children.Remove(_dragRow);
                ActiveToolsList.Children.Insert(targetIndex, _dragRow);
            }
        }
        else
        {
            // Normal reorder — but don't allow dropping into the Settings row's slot
            int targetIndex = GetDropIndex(pos.Y);
            int currentIndex = ActiveToolsList.Children.IndexOf(_dragRow);

            // Clamp so we never land on the Settings row's position
            var rows = ActiveToolsList.Children.OfType<Border>().ToList();
            int settingsIdx = rows.FindIndex(b => b.Tag is string s && s == SettingsTag);
            if (settingsIdx == 0 && targetIndex == 0) targetIndex = 1;
            else if (settingsIdx == rows.Count - 1 && targetIndex == rows.Count - 1) targetIndex = rows.Count - 2;

            if (targetIndex != currentIndex && targetIndex >= 0 && targetIndex < ActiveToolsList.Children.Count)
            {
                ActiveToolsList.Children.Remove(_dragRow);
                ActiveToolsList.Children.Insert(targetIndex, _dragRow);
            }
        }
    }

    private void ToolRow_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_dragRow == null) return;
        _dragRow.ReleaseMouseCapture();

        if (_isDraggingToolRow)
        {
            var rows = ActiveToolsList.Children.OfType<Border>().ToList();
            var settingsRow = rows.FirstOrDefault(b => b.Tag is string st && st == SettingsTag);
            var toolRows = rows.Where(b => b.Tag is ToolType).ToList();

            bool settingsAtTop = false;
            if (settingsRow != null)
            {
                int settingsIdx = rows.IndexOf(settingsRow);
                int midpoint = rows.Count / 2;
                settingsAtTop = settingsIdx < midpoint;

                // Clamp Settings to top or bottom in UI
                rows.Remove(settingsRow);
                if (settingsAtTop) rows.Insert(0, settingsRow);
                else rows.Add(settingsRow);
                ActiveToolsList.Children.Clear();
                foreach (var r in rows) ActiveToolsList.Children.Add(r);
            }

            var toolPart = SettingsService.SerializeToolOrder(toolRows.Select(b => (ToolType)b.Tag!).ToList());
            bool settingsPresent = settingsRow != null;
            _settings.ToolOrder = settingsPresent
                ? (settingsAtTop ? "Settings," + toolPart : toolPart + ",Settings")
                : toolPart;
            _settings.Save();
        }

        _dragRow = null;
        _isDraggingToolRow = false;
        e.Handled = true;
    }

    private void ToolRow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { }

    private int GetDropIndex(double y)
    {
        int i = 0;
        foreach (Border child in ActiveToolsList.Children.OfType<Border>())
        {
            var pos = child.TranslatePoint(new System.Windows.Point(0, child.ActualHeight / 2), ActiveToolsList);
            if (y < pos.Y) return i;
            i++;
        }
        return ActiveToolsList.Children.Count - 1;
    }

    private void ShowToolbarCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.FlyoutToolbarVisible = ShowToolbarCheck.IsChecked == true;
        _settings.Save();
    }

    private void ResetToolbarSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToolbarSettings();
        _isInitializing = true;
        ShowToolbarCheck.IsChecked = _settings.FlyoutToolbarVisible;
        ShowToolNameCheck.IsChecked = _settings.ShowToolNameOnSwitch;
        _isInitializing = false;
        RebuildToolLists();
    }

}
