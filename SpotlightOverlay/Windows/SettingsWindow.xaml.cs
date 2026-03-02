using System.Windows;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Windows;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private readonly SettingsService _settings;
    private bool _isInitializing;

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

        _isInitializing = false;
    }

    public static void ShowSingleton(SettingsService settings)
    {
        if (_instance is { IsLoaded: true })
        {
            _instance.Activate();
            return;
        }
        _instance = new SettingsWindow(settings);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        var pct = (int)Math.Clamp(e.NewValue, 1, 99);
        _settings.OverlayOpacity = pct / 100.0;
        _settings.Save();
        OpacityTextBox.Text = pct.ToString() + "%";
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
    }

    private void DragStyleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.DragStyle = (Models.DragStyle)DragStyleCombo.SelectedIndex;
        _settings.Save();
    }

}
