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
        SpotlightModeCombo.SelectedIndex = _settings.CumulativeSpotlights ? 0 : 1;
        ArrowheadStyleCombo_Init();
        BuildColorPresetSwatches();
        LoadCustomColors();
        BuildCustomColorSlots();
        InitSizeControls();
        SyncStyleCheck_Init();
        SyncSizeCheck_Init();
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        UpdateDragStyleLabels();

        _isInitializing = false;
        UpdateSpotlightModeHint();
        Loaded += (_, _) => { UpdatePreview(); UpdateArrowPreview(); };
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
            DragStyleClick.Content = $"{prefix} to {prefix}";
        }
        else
        {
            DragStyleHold.Content = $"{prefix} + Hold and Drag";
            DragStyleClick.Content = $"{prefix} + Click to Click";
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

    private void SpotlightModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.CumulativeSpotlights = SpotlightModeCombo.SelectedIndex == 0;
        _settings.Save();
        UpdateSpotlightModeHint();
    }

    private void UpdateSpotlightModeHint()
    {
        if (SpotlightModeHint == null) return;
        SpotlightModeHint.Text = _settings.CumulativeSpotlights
            ? "New spotlights are added alongside existing ones"
            : "Each new spotlight replaces the previous one";
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
        SizeLeftBtn.Opacity = leftEnabled ? 1.0 : 0.2;
        SizeLeftBtn.Cursor = leftEnabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        SizeRightBtn.IsEnabled = rightEnabled;
        SizeRightBtn.Opacity = rightEnabled ? 1.0 : 0.2;
        SizeRightBtn.Cursor = rightEnabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;

        SizeLeftBtn.Background = (_currentSizeTarget == SizeTarget.LeftEnd && leftEnabled) ? accent : normal;
        SizeLeftBtn.BorderBrush = (_currentSizeTarget == SizeTarget.LeftEnd && leftEnabled) ? accent : borderNormal;
        SizeLineBtn.Background = _currentSizeTarget == SizeTarget.Line ? accent : normal;
        SizeLineBtn.BorderBrush = _currentSizeTarget == SizeTarget.Line ? accent : borderNormal;
        SizeRightBtn.Background = (_currentSizeTarget == SizeTarget.RightEnd && rightEnabled) ? accent : normal;
        SizeRightBtn.BorderBrush = (_currentSizeTarget == SizeTarget.RightEnd && rightEnabled) ? accent : borderNormal;
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
        _settings.ResetToDefaults();

        _isInitializing = true;
        OpacitySlider.Value = 100 - _settings.OverlayOpacity * 100;
        OpacityTextBox.Text = ((int)(100 - _settings.OverlayOpacity * 100)).ToString() + "%";
        FeatherSlider.Value = _settings.FeatherRadius;
        FeatherTextBox.Text = _settings.FeatherRadius.ToString();
        PreviewStyleCombo.SelectedIndex = (int)_settings.PreviewStyle;
        DragStyleCombo.SelectedIndex = (int)_settings.DragStyle;
        BackgroundCombo.SelectedIndex = _settings.FreezeScreen ? 1 : 0;
        SpotlightModeCombo.SelectedIndex = _settings.CumulativeSpotlights ? 0 : 1;
        ArrowheadStyleCombo_Init();
        HighlightSelectedPreset();
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        UpdateDragStyleLabels();
        _isInitializing = false;

        UpdateSpotlightModeHint();
        UpdatePreview();
        UpdateArrowPreview();
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
        HotkeyHint.Text = "Click to change — press keys or mouse buttons";
        HotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
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
            HotkeyHint.Text = "⚠ Same as Toggle On/Off hotkey — pick a different combo";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
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
            return;
        }

        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        // Check conflict with toggle hotkey
        if (mod.Value == _settings.ToggleModifier && vk == _settings.ToggleKey)
        {
            HotkeyHint.Text = "⚠ Same as Toggle On/Off hotkey — pick a different combo";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
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
            HotkeyHint.Text = "⚠ Would conflict with Toggle On/Off hotkey";
            HotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
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
        }
    }

    // ── Toggle hotkey recorder ─────────────────────────────────────

    private bool _isRecordingToggleHotkey;
    private Models.ModifierKey? _pendingToggleMod;

    public static string VkDisplayName(int vk)
    {
        // Mouse buttons
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

    private static bool IsMouseButtonVk(int vk) => vk is 0x04 or 0x05 or 0x06;

    private void UpdateToggleHotkeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        var modDisplay = ModifierDisplayName(_settings.ToggleModifier);
        if (!string.IsNullOrEmpty(modDisplay)) parts.Add(modDisplay);
        parts.Add(VkDisplayName(_settings.ToggleKey));
        ToggleHotkeyDisplay.Text = string.Join(" + ", parts);
        ToggleHotkeyHint.Text = "Click to change — press keys or mouse buttons";
        ToggleHotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
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
            return;
        }

        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        // Check conflict with activation hotkey
        if (mod.Value == _settings.ActivationModifier && vk == _settings.ActivationKey)
        {
            ToggleHotkeyHint.Text = "⚠ Same as Activation hotkey — pick a different combo";
            ToggleHotkeyHint.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
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
                    swatch.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
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
    }

    private void UpdateSyncStyleCheckEnabled()
    {
        bool hasTwoEnds = _settings.ArrowheadStyle != ArrowheadStyle.None && _settings.ArrowEndStyle != ArrowheadStyle.None;
        SyncStyleCheck.IsEnabled = hasTwoEnds;
        SyncStyleCheck.Opacity = hasTwoEnds ? 1.0 : 0.4;
    }

    private void UpdateSyncSizeCheckEnabled()
    {
        bool hasTwoEnds = _settings.ArrowheadStyle != ArrowheadStyle.None && _settings.ArrowEndStyle != ArrowheadStyle.None;
        SyncSizeCheck.IsEnabled = hasTwoEnds;
        SyncSizeCheck.Opacity = hasTwoEnds ? 1.0 : 0.4;
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

}
