using System.Windows;
using SpotlightOverlay.Services;

namespace SpotlightOverlay.Windows;

/// <summary>
/// Settings dialog for adjusting Overlay Opacity and Feather Radius.
/// Implements singleton pattern via a static instance reference.
/// </summary>
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

        // Load current values from SettingsService (Req 9.2)
        OpacitySlider.Value = _settings.OverlayOpacity;
        OpacityValueText.Text = _settings.OverlayOpacity.ToString("F2");

        FeatherSlider.Value = _settings.FeatherRadius;
        FeatherValueText.Text = _settings.FeatherRadius.ToString();

        _isInitializing = false;
    }

    /// <summary>
    /// Opens the SettingsWindow or brings the existing instance to the foreground (Req 9.4).
    /// </summary>
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

        _settings.OverlayOpacity = e.NewValue;
        _settings.Save(); // Req 9.3, 8.4
        OpacityValueText.Text = e.NewValue.ToString("F2");
    }

    private void FeatherSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        _settings.FeatherRadius = (int)e.NewValue;
        _settings.Save(); // Req 9.3, 8.4
        FeatherValueText.Text = ((int)e.NewValue).ToString();
    }
}
