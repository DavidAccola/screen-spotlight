using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SpotlightOverlay.Services;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace SpotlightOverlay.Windows;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private readonly SettingsService _settings;
    private bool _isInitializing;
    private System.Windows.Threading.DispatcherTimer? _animTimer;
    private int _animFrame;

    public SettingsWindow(SettingsService settings)
    {
        _settings = settings;
        _isInitializing = true;
        InitializeComponent();

        OpacitySlider.Value = _settings.OverlayOpacity * 100;
        OpacityTextBox.Text = ((int)(_settings.OverlayOpacity * 100)).ToString() + "%";
        FeatherSlider.Value = _settings.FeatherRadius;
        FeatherTextBox.Text = _settings.FeatherRadius.ToString();
        PreviewStyleCombo.SelectedIndex = (int)_settings.PreviewStyle;
        DragStyleCombo.SelectedIndex = (int)_settings.DragStyle;
        FreezeToggle.IsChecked = _settings.FreezeScreen;

        _isInitializing = false;
        UpdatePreview();
    }

    public static void ShowSingleton(SettingsService settings)
    {
        if (_instance is { IsLoaded: true }) { _instance.Activate(); return; }
        _instance = new SettingsWindow(settings);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    // ── Preview constants ──────────────────────────────────────────

    private const double CanvasW = 386, CanvasH = 130;
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
        string label = _settings.DragStyle == Models.DragStyle.HoldDrag
            ? "Hold Ctrl + Click and drag"
            : "Hold Ctrl > Click to start > Click to end";
        bool dark = _settings.OverlayOpacity <= 0.3;
        var tb = new TextBlock
        {
            Text = label,
            Foreground = dark ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White,
            FontSize = 9, Opacity = dark ? 0.6 : 0.5, IsHitTestVisible = false
        };
        tb.Measure(new Size(CanvasW, CanvasH));
        Canvas.SetLeft(tb, CanvasW - tb.DesiredSize.Width - 6);
        Canvas.SetTop(tb, CanvasH - tb.DesiredSize.Height - 4);
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

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        var pct = (int)Math.Clamp(e.NewValue, 1, 99);
        _settings.OverlayOpacity = pct / 100.0;
        _settings.Save();
        OpacityTextBox.Text = pct.ToString() + "%";
        UpdatePreview();
    }

    private void ApplyOpacityFromTextBox()
    {
        var text = OpacityTextBox.Text.TrimEnd('%', ' ');
        if (int.TryParse(text, out int pct))
        {
            pct = Math.Clamp(pct, 1, 99);
            _isInitializing = true;
            OpacitySlider.Value = pct;
            _isInitializing = false;
            _settings.OverlayOpacity = pct / 100.0;
            _settings.Save();
            OpacityTextBox.Text = pct.ToString() + "%";
            UpdatePreview();
        }
        else
        {
            OpacityTextBox.Text = ((int)(_settings.OverlayOpacity * 100)).ToString() + "%";
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

    private void FreezeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.FreezeScreen = FreezeToggle.IsChecked == true;
        _settings.Save();
    }
}
