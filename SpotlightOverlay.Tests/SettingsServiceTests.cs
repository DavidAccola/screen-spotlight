using System.IO;
using System.Text.Json;
using SpotlightOverlay.Models;
using SpotlightOverlay.Services;
using Xunit;

namespace SpotlightOverlay.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpotlightOverlayTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string GetSettingsPath() => Path.Combine(_tempDir, "Settings.json");

    /// <summary>
    /// Validates: Requirement 8.1
    /// When Settings.json exists with valid data, Load() reads and applies those values.
    /// </summary>
    [Fact]
    public void Load_ExistingValidFile_AppliesValues()
    {
        var path = GetSettingsPath();
        var settings = new AppSettings(0.8, 50, PreviewStyle.Crosshair, DragStyle.ClickClick, false, ModifierKey.Ctrl, 0, ModifierKey.CtrlShift, 0x51);
        File.WriteAllText(path, JsonSerializer.Serialize(settings));

        var service = new SettingsService(path);
        service.Load();

        Assert.Equal(0.8, service.OverlayOpacity);
        Assert.Equal(50, service.FeatherRadius);
    }

    /// <summary>
    /// Validates: Requirement 8.2
    /// When Settings.json does not exist, Load() creates it with defaults (opacity=0.5, radius=30).
    /// </summary>
    [Fact]
    public void Load_MissingFile_CreatesDefaultsFile()
    {
        var path = GetSettingsPath();
        Assert.False(File.Exists(path));

        var service = new SettingsService(path);
        service.Load();

        Assert.Equal(0.75, service.OverlayOpacity);
        Assert.Equal(8, service.FeatherRadius);

        // Verify the file was created with default values
        Assert.True(File.Exists(path));
        var written = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        Assert.NotNull(written);
        Assert.Equal(0.75, written!.OverlayOpacity);
        Assert.Equal(8, written.FeatherRadius);
    }

    /// <summary>
    /// Validates: Requirement 8.3
    /// When Settings.json contains invalid JSON, Load() logs warning and uses defaults.
    /// </summary>
    [Fact]
    public void Load_InvalidJson_UsesDefaults()
    {
        var path = GetSettingsPath();
        File.WriteAllText(path, "{ this is not valid json!!!");

        var service = new SettingsService(path);
        service.Load();

        Assert.Equal(0.75, service.OverlayOpacity);
        Assert.Equal(8, service.FeatherRadius);
    }

    /// <summary>
    /// Validates: Requirements 1.2, 1.3, 1.4, 1.5
    /// Saving NubFraction, NubAnchorEdge, and NubMonitorFingerprint and reloading
    /// must produce identical values.
    /// </summary>
    [Fact]
    public void NubFields_RoundTrip_PreservesValues()
    {
        var path = GetSettingsPath();
        var svc = new SettingsService(path);
        svc.Load(); // creates defaults

        svc.NubFraction = 0.35;
        svc.NubAnchorEdge = AnchorEdge.Left;
        svc.NubMonitorFingerprint = @"\\.\DISPLAY1|1920x1080";
        svc.Save();

        var svc2 = new SettingsService(path);
        svc2.Load();

        Assert.Equal(0.35, svc2.NubFraction);
        Assert.Equal(AnchorEdge.Left, svc2.NubAnchorEdge);
        Assert.Equal(@"\\.\DISPLAY1|1920x1080", svc2.NubMonitorFingerprint);
    }

    /// <summary>
    /// Validates: Requirements 1.2, 4.1
    /// Saving NubFraction = null and reloading must return null (no saved position).
    /// </summary>
    [Fact]
    public void NubFraction_Null_RoundTrip_RemainsNull()
    {
        var path = GetSettingsPath();
        var svc = new SettingsService(path);
        svc.Load();

        svc.NubFraction = null;
        svc.Save();

        var svc2 = new SettingsService(path);
        svc2.Load();

        Assert.Null(svc2.NubFraction);
    }
}
