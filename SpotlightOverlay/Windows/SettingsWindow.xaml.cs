using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SpotlightOverlay.Input;
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
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        UpdateDragStyleLabels();

        _isInitializing = false;
        Loaded += (_, _) => UpdatePreview();
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
        DrawDragStyleLabel();
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
            DrawDragStyleLabel();
        }
        else if (_animFrame <= DragFrames + HoldFrames)
        {
            DrawFeatheredOverlay(CutoutFinal);
            DrawStyleIndicators(CutoutFinal);
            DrawDragStyleLabel();
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
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        UpdateDragStyleLabels();
        _isInitializing = false;

        UpdatePreview();
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

        if (key == System.Windows.Input.Key.Escape)
        {
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

        e.Handled = true;

        if (key == System.Windows.Input.Key.Escape)
        {
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

}
