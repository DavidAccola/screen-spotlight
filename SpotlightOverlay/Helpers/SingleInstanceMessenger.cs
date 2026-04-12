using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SpotlightOverlay.Helpers;

/// <summary>
/// Handles single-instance communication via a hidden message-only window.
/// The first instance creates the window and listens for activation requests.
/// Subsequent instances (e.g. pinned taskbar clicks) find the window and post the message, then exit.
/// </summary>
public sealed class SingleInstanceMessenger : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint RegisterWindowMessage(string lpString);

    // Unique window title used to find the first instance's message window
    private const string MessageWindowTitle = "SpotlightOverlay_MessageWindow";

    private readonly uint _activateMsg;
    private HwndSource? _hwndSource;

    public event Action? ActivateRequested;

    public SingleInstanceMessenger()
    {
        _activateMsg = RegisterWindowMessage("SpotlightOverlay_Activate");
    }

    /// <summary>
    /// Creates the hidden message-only window. Call this in the first instance only.
    /// </summary>
    public void CreateMessageWindow()
    {
        var p = new HwndSourceParameters(MessageWindowTitle)
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE — message-only window
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        _hwndSource = new HwndSource(p);
        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// Sends the activate message to the first instance's message window.
    /// Call this from a second instance before shutting down.
    /// Returns true if the message was delivered.
    /// </summary>
    public bool NotifyFirstInstance()
    {
        IntPtr hwnd = FindWindow(null, MessageWindowTitle);
        if (hwnd == IntPtr.Zero) return false;
        PostMessage(hwnd, _activateMsg, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _activateMsg)
        {
            ActivateRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}
