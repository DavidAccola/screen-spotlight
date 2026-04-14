using System.IO;

namespace SpotlightOverlay;

/// <summary>
/// Simple file-based debug logger. Writes to spotlight-debug.log in the app directory.
/// Only active in DEBUG builds — no-ops in Release.
/// </summary>
internal static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "spotlight-debug.log");
    private static readonly object Lock = new();

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Write(string message)
    {
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Swallow — don't crash the app for logging
            }
        }
    }
}
