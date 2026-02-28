using System.IO;
using System.Text.Json;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Services;

/// <summary>
/// Loads, saves, validates, and exposes user-configurable settings from a JSON file.
/// </summary>
public class SettingsService
{
    private const double DefaultOverlayOpacity = 0.5;
    private const int DefaultFeatherRadius = 30;
    private const string SettingsFileName = "Settings.json";

    private readonly string _settingsFilePath;

    public double OverlayOpacity { get; set; } = DefaultOverlayOpacity;
    public int FeatherRadius { get; set; } = DefaultFeatherRadius;

    public SettingsService()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName))
    {
    }

    /// <summary>
    /// Constructor accepting a custom file path, primarily for testing.
    /// </summary>
    public SettingsService(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    /// <summary>
    /// Loads settings from the JSON file. Creates defaults if the file is missing.
    /// Uses defaults and logs a warning if the file contains invalid JSON.
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            OverlayOpacity = DefaultOverlayOpacity;
            FeatherRadius = DefaultFeatherRadius;
            Save();
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(_settingsFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Failed to read settings file: {ex.Message}. Using defaults.");
            OverlayOpacity = DefaultOverlayOpacity;
            FeatherRadius = DefaultFeatherRadius;
            return;
        }

        AppSettings settings;
        try
        {
            settings = Deserialize(json);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Invalid JSON in settings file: {ex.Message}. Using defaults.");
            OverlayOpacity = DefaultOverlayOpacity;
            FeatherRadius = DefaultFeatherRadius;
            return;
        }

        var validated = Validate(settings);
        OverlayOpacity = validated.OverlayOpacity;
        FeatherRadius = validated.FeatherRadius;
    }

    /// <summary>
    /// Saves the current settings to the JSON file.
    /// </summary>
    public void Save()
    {
        var settings = new AppSettings(OverlayOpacity, FeatherRadius);
        var json = Serialize(settings);
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Deserializes a JSON string into an AppSettings record.
    /// </summary>
    public static AppSettings Deserialize(string json)
    {
        return JsonSerializer.Deserialize<AppSettings>(json)
            ?? new AppSettings(DefaultOverlayOpacity, DefaultFeatherRadius);
    }

    /// <summary>
    /// Serializes an AppSettings record to a JSON string.
    /// </summary>
    public static string Serialize(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings);
    }

    /// <summary>
    /// Validates and clamps settings values to valid ranges.
    /// OverlayOpacity is clamped to [0.0, 1.0].
    /// FeatherRadius is clamped to [0, int.MaxValue].
    /// </summary>
    public static AppSettings Validate(AppSettings settings)
    {
        var opacity = double.IsNaN(settings.OverlayOpacity) || double.IsInfinity(settings.OverlayOpacity)
            ? DefaultOverlayOpacity
            : Math.Clamp(settings.OverlayOpacity, 0.0, 1.0);
        var radius = Math.Max(settings.FeatherRadius, 0);
        return new AppSettings(opacity, radius);
    }
}
