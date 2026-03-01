using System.Diagnostics;
using System.Runtime.InteropServices;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Input;

/// <summary>
/// Registers low-level mouse and keyboard hooks via SetWindowsHookEx
/// to capture global input events for spotlight gesture detection.
/// </summary>
public class GlobalInputHook : IDisposable
{
    #region P/Invoke

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_ESCAPE = 0x1B;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when a Ctrl+Click+Drag gesture completes with the resulting rectangle.
    /// </summary>
    public event EventHandler<DragRectEventArgs>? DragCompleted;

    /// <summary>
    /// Raised during a Ctrl+Click+Drag gesture with the current rectangle as the user moves the mouse.
    /// </summary>
    public event EventHandler<DragRectEventArgs>? DragUpdated;

    /// <summary>
    /// Raised when a drag gesture is cancelled (e.g., right-click during drag).
    /// </summary>
    public event EventHandler? DragCancelled;

    /// <summary>
    /// Raised when the Ctrl key is released after one or more drag gestures.
    /// </summary>
    public event EventHandler? CtrlReleased;

    /// <summary>
    /// Raised when the Escape key is pressed to dismiss the overlay.
    /// </summary>
    public event EventHandler? DismissRequested;

    #endregion

    /// <summary>
    /// When false, all input events are ignored (hooks remain installed but inactive).
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Optional callback for reporting hook installation errors.
    /// Wired to tray balloon notifications by the application layer.
    /// </summary>
    public Action<string>? OnError { get; set; }

    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;

    // Must be stored as fields to prevent garbage collection of the delegates
    private LowLevelProc? _mouseProc;
    private LowLevelProc? _keyboardProc;

    // Drag tracking state
    private bool _isDragging;
    private System.Windows.Point _dragStartPoint;
    private bool _hasPendingDrags; // true if any drags completed while Ctrl was held

    private bool _disposed;

    /// <summary>
    /// Installs the low-level mouse and keyboard hooks.
    /// </summary>
    public void Install()
    {
        if (_mouseHookHandle != IntPtr.Zero || _keyboardHookHandle != IntPtr.Zero)
        {
            SpotlightOverlay.DebugLog.Write("[Hook] Already installed, skipping.");
            return;
        }

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
        SpotlightOverlay.DebugLog.Write($"[Hook] Module handle: {moduleHandle}");

        _mouseProc = MouseHookCallback;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            string error = $"Failed to install mouse hook. Error code: {Marshal.GetLastWin32Error()}";
            SpotlightOverlay.DebugLog.Write($"[Hook] ERROR: {error}");
            OnError?.Invoke(error);
            return;
        }
        SpotlightOverlay.DebugLog.Write($"[Hook] Mouse hook installed: {_mouseHookHandle}");

        _keyboardProc = KeyboardHookCallback;
        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            _mouseProc = null;

            string error = $"Failed to install keyboard hook. Error code: {Marshal.GetLastWin32Error()}";
            SpotlightOverlay.DebugLog.Write($"[Hook] ERROR: {error}");
            OnError?.Invoke(error);
        }
        else
        {
            SpotlightOverlay.DebugLog.Write($"[Hook] Keyboard hook installed: {_keyboardHookHandle}");
        }
    }

    /// <summary>
    /// Uninstalls the low-level mouse and keyboard hooks.
    /// </summary>
    public void Uninstall()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            _mouseProc = null;
        }

        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
            _keyboardProc = null;
        }
    }

    /// <summary>
    /// Low-level mouse hook callback. Detects Ctrl+Click+Drag gesture.
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int msg = wParam.ToInt32();

            if (msg == WM_LBUTTONDOWN)
            {
                short ctrlState = GetAsyncKeyState(VK_CONTROL);
                SpotlightOverlay.DebugLog.Write($"[Hook] LButtonDown, Ctrl state: 0x{ctrlState:X4}, Ctrl held: {(ctrlState & 0x8000) != 0}");
                if ((ctrlState & 0x8000) != 0)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _isDragging = true;
                    _dragStartPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);
                    SpotlightOverlay.DebugLog.Write($"[Hook] Drag started at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                    return (IntPtr)1; // Suppress — don't pass Ctrl+Click to other apps
                }
            }
            else if (msg == WM_LBUTTONUP)
            {
                SpotlightOverlay.DebugLog.Write($"[Hook] LButtonUp, _isDragging: {_isDragging}");
                if (_isDragging)
                {
                    _isDragging = false;
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var endPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);

                    double x = Math.Min(_dragStartPoint.X, endPoint.X);
                    double y = Math.Min(_dragStartPoint.Y, endPoint.Y);
                    double width = Math.Abs(endPoint.X - _dragStartPoint.X);
                    double height = Math.Abs(endPoint.Y - _dragStartPoint.Y);

                    SpotlightOverlay.DebugLog.Write($"[Hook] Drag ended at ({hookStruct.pt.x}, {hookStruct.pt.y}), rect: ({x},{y},{width},{height})");

                    if (width > 1 && height > 1)
                    {
                        var rect = new System.Windows.Rect(x, y, width, height);
                        SpotlightOverlay.DebugLog.Write($"[Hook] Emitting DragCompleted: {rect}");
                        DragCompleted?.Invoke(this, new DragRectEventArgs(rect, _dragStartPoint));
                        _hasPendingDrags = true;
                    }
                    else
                    {
                        SpotlightOverlay.DebugLog.Write($"[Hook] Drag too small, ignoring (w={width}, h={height})");
                    }

                    return (IntPtr)1; // Suppress — don't pass button-up to other apps
                }
            }
            else if (msg == WM_MOUSEMOVE)
            {
                if (_isDragging)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var currentPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);

                    double x = Math.Min(_dragStartPoint.X, currentPoint.X);
                    double y = Math.Min(_dragStartPoint.Y, currentPoint.Y);
                    double width = Math.Abs(currentPoint.X - _dragStartPoint.X);
                    double height = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

                    if (width > 1 && height > 1)
                    {
                        var rect = new System.Windows.Rect(x, y, width, height);
                        DragUpdated?.Invoke(this, new DragRectEventArgs(rect, _dragStartPoint));
                    }

                    return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam); // Let mouse move pass through so cursor still tracks
                }
            }
            else if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP)
            {
                if (_isDragging)
                {
                    SpotlightOverlay.DebugLog.Write("[Hook] Right-click detected, cancelling drag");
                    DragCancelled?.Invoke(this, EventArgs.Empty);
                }
                _isDragging = false;
            }
        }
        else if (nCode >= 0 && !IsEnabled)
        {
            _isDragging = false;
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Low-level keyboard hook callback. Detects Escape key press to dismiss overlay.
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int msg = wParam.ToInt32();
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (msg == WM_KEYDOWN)
            {
                if (hookStruct.vkCode == VK_ESCAPE)
                {
                    DismissRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (msg == WM_KEYUP)
            {
                if ((hookStruct.vkCode == VK_CONTROL || hookStruct.vkCode == VK_LCONTROL || hookStruct.vkCode == VK_RCONTROL) && _hasPendingDrags)
                {
                    _hasPendingDrags = false;
                    SpotlightOverlay.DebugLog.Write("[Hook] Ctrl released with pending drags, emitting CtrlReleased");
                    CtrlReleased?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
