using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpotlightOverlay.Windows;

public partial class ColorPickerDialog : Window
{
    private double _currentHue;
    private double _currentSat;
    private double _currentVal;
    private bool _isDraggingSv;
    private bool _isDraggingHue;

    /// <summary>The selected color hex (6 chars, no #). Null if cancelled.</summary>
    public string? SelectedHex { get; private set; }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public ColorPickerDialog(string initialHex)
    {
        InitializeComponent();

        if (new WindowInteropHelper(this).EnsureHandle() is var hwnd && hwnd != IntPtr.Zero)
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
            int captionColor = 0x1E1E1E;
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
        }

        HexColorInput.Text = initialHex;
        ApplyHexToHsv(initialHex);
        UpdatePreview();

        SvContainer.MouseMove += SvContainer_MouseMove;
        HueBarBorder.MouseMove += HueBar_MouseMove;

        Loaded += (_, _) => UpdateHsvIndicators();
    }

    private void ApplyHexToHsv(string hex)
    {
        try
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var (h, s, v) = SettingsWindow.RgbToHsv(color);
            _currentHue = h;
            _currentSat = s;
            _currentVal = v;
            SvHueLayer.Background = new SolidColorBrush(SettingsWindow.HsvToRgb(_currentHue, 1, 1));
        }
        catch { _currentHue = 0; _currentSat = 1; _currentVal = 1; }
    }

    private void UpdatePreview()
    {
        var color = SettingsWindow.HsvToRgb(_currentHue, _currentSat, _currentVal);
        ColorPreview.Background = new SolidColorBrush(color);
        HexColorInput.Text = $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void UpdateHsvIndicators()
    {
        double svW = SvContainer.ActualWidth;
        double svH = SvContainer.ActualHeight;
        if (svW > 0 && svH > 0)
        {
            Canvas.SetLeft(SvCrosshair, _currentSat * svW - 6);
            Canvas.SetTop(SvCrosshair, (1.0 - _currentVal) * svH - 6);
        }
        double hueH = HueBarBorder.ActualHeight;
        if (hueH > 0)
            Canvas.SetTop(HueIndicator, (_currentHue / 360.0) * hueH - 2);
    }

    // SV square handlers
    private void SvContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = true;
        SvContainer.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvContainer));
    }
    private void SvContainer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSv) UpdateSvFromMouse(e.GetPosition(SvContainer));
    }
    private void SvContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = false;
        SvContainer.ReleaseMouseCapture();
    }
    private void UpdateSvFromMouse(Point pos)
    {
        double w = SvContainer.ActualWidth, h = SvContainer.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _currentSat = Math.Clamp(pos.X / w, 0, 1);
        _currentVal = Math.Clamp(1.0 - pos.Y / h, 0, 1);
        UpdatePreview();
        UpdateHsvIndicators();
    }

    // Hue bar handlers
    private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        HueBarBorder.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueBarBorder));
    }
    private void HueBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingHue) UpdateHueFromMouse(e.GetPosition(HueBarBorder));
    }
    private void HueBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = false;
        HueBarBorder.ReleaseMouseCapture();
    }
    private void UpdateHueFromMouse(Point pos)
    {
        double h = HueBarBorder.ActualHeight;
        if (h <= 0) return;
        _currentHue = Math.Clamp(pos.Y / h, 0, 1) * 360.0;
        SvHueLayer.Background = new SolidColorBrush(SettingsWindow.HsvToRgb(_currentHue, 1, 1));
        UpdatePreview();
        UpdateHsvIndicators();
    }

    // Hex input
    private void HexColorInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyHex();
    }
    private void HexColorInput_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();
    private void ApplyHex()
    {
        var text = HexColorInput.Text.Trim();
        if (text.Length == 6 && IsValidHex(text))
        {
            ApplyHexToHsv(text);
            UpdatePreview();
            UpdateHsvIndicators();
        }
        else
        {
            UpdatePreview(); // revert display
        }
    }
    private static bool IsValidHex(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        return true;
    }

    // OK / Cancel
    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var color = SettingsWindow.HsvToRgb(_currentHue, _currentSat, _currentVal);
        SelectedHex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
