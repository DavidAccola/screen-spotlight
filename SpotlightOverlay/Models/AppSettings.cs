namespace SpotlightOverlay.Models;

/// <summary>
/// Immutable settings record for the Spotlight Overlay application.
/// OverlayOpacity: [0.0, 1.0], default 0.5
/// FeatherRadius: [0, ∞), default 30
/// </summary>
public record AppSettings(double OverlayOpacity, int FeatherRadius);
