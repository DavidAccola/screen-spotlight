using System.Diagnostics;
using System.Runtime.InteropServices;
using SpotlightOverlay.Models;

namespace SpotlightOverlay.Input;

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
        public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    #endregion

    #region Events
    public event EventHandler<DragRectEventArgs>? DragCompleted;
    public event EventHandler<DragRectEventArgs>? DragUpdated;
    public event EventHandler? DragCancelled;
    public event EventHandler? CtrlReleased;
    public event EventHandler? RestoreRequested;
    public event EventHandler? DismissRequested;
    #endregion

    public bool IsEnabled { get; set; }
    public bool CanRestore { get; set; }
    public DragStyle DragStyle { get; set; }
    public Action<string>? OnError { get; set; }

    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private LowLevelProc? _mouseProc;
    private LowLevelProc? _keyboardProc;

    // Drag tracking state
    private bool _isDragging;
    private System.Windows.Point _dragStartPoint;
    private bool _hasPendingDrags;
    // Click-click mode: waiting for second click after first Ctrl+click set the start
    private bool _isClickClickActive;

    private bool _disposed;

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

        _mouseProc = MouseHookCallback;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            OnError?.Invoke($"Failed to install mouse hook. Error: {Marshal.GetLastWin32Error()}");
            return;
        }

        _keyboardProc = KeyboardHookCallback;
        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero; _mouseProc = null;
            OnError?.Invoke($"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            SpotlightOverlay.DebugLog.Write("[Hook] Hooks installed");
        }
    }

    public void Uninstall()
    {
        if (_mouseHookHandle != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookHandle); _mouseHookHandle = IntPtr.Zero; _mouseProc = null; }
        if (_keyboardHookHandle != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHookHandle); _keyboardHookHandle = IntPtr.Zero; _keyboardProc = null; }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int msg = wParam.ToInt32();

            if (DragStyle == DragStyle.ClickClick)
                return HandleClickClickMouse(msg, lParam, nCode, wParam);
            else
                return HandleHoldDragMouse(msg, lParam, nCode, wParam);
        }
        else if (nCode >= 0 && !IsEnabled)
        {
            _isDragging = false;
            _isClickClickActive = false;
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    /// <summary>Hold-drag mode: Ctrl+click down, drag, release = cutout</summary>
    private IntPtr HandleHoldDragMouse(int msg, IntPtr lParam, int nCode, IntPtr wParam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            short ctrlState = GetAsyncKeyState(VK_CONTROL);
            if ((ctrlState & 0x8000) != 0)
            {
                var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _isDragging = true;
                _dragStartPoint = new System.Windows.Point(hs.pt.x, hs.pt.y);
                SpotlightOverlay.DebugLog.Write($"[Hook] HoldDrag: start at {hs.pt.x},{hs.pt.y}");
                return (IntPtr)1;
            }
        }
        else if (msg == WM_LBUTTONUP && _isDragging)
        {
            _isDragging = false;
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            SpotlightOverlay.DebugLog.Write($"[Hook] HoldDrag: end at {hs.pt.x},{hs.pt.y}");
            EmitDragCompleted(hs.pt.x, hs.pt.y);
            return (IntPtr)1;
        }
        else if (msg == WM_MOUSEMOVE && _isDragging)
        {
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            EmitDragUpdated(hs.pt.x, hs.pt.y);
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }
        else if ((msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP) && _isDragging)
        {
            _isDragging = false;
            SpotlightOverlay.DebugLog.Write("[Hook] HoldDrag: cancelled by right-click");
            DragCancelled?.Invoke(this, EventArgs.Empty);
            return (IntPtr)1;
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    /// <summary>Click-click mode: Ctrl+click = start, move, click = end. Right-click cancels.</summary>
    private IntPtr HandleClickClickMouse(int msg, IntPtr lParam, int nCode, IntPtr wParam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            if (_isClickClickActive)
            {
                // Second click — complete the cutout
                var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _isClickClickActive = false;
                SpotlightOverlay.DebugLog.Write($"[Hook] ClickClick: second click at {hs.pt.x},{hs.pt.y}");
                EmitDragCompleted(hs.pt.x, hs.pt.y);

                // If Ctrl is still held, respect batch mode (wait for Ctrl release)
                // If Ctrl is NOT held, apply immediately
                short ctrlNow = GetAsyncKeyState(VK_CONTROL);
                if ((ctrlNow & 0x8000) == 0)
                {
                    SpotlightOverlay.DebugLog.Write("[Hook] ClickClick: Ctrl not held, applying immediately");
                    _hasPendingDrags = false;
                    CtrlReleased?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SpotlightOverlay.DebugLog.Write("[Hook] ClickClick: Ctrl held, batching");
                }
                return (IntPtr)1;
            }
            else
            {
                short ctrlState = GetAsyncKeyState(VK_CONTROL);
                if ((ctrlState & 0x8000) != 0)
                {
                    // First Ctrl+click — set start point
                    var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _isClickClickActive = true;
                    _dragStartPoint = new System.Windows.Point(hs.pt.x, hs.pt.y);
                    SpotlightOverlay.DebugLog.Write($"[Hook] ClickClick: first click at {hs.pt.x},{hs.pt.y}");
                    return (IntPtr)1;
                }
            }
        }
        else if (msg == WM_MOUSEMOVE && _isClickClickActive)
        {
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            EmitDragUpdated(hs.pt.x, hs.pt.y);
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }
        else if ((msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP) && _isClickClickActive)
        {
            _isClickClickActive = false;
            SpotlightOverlay.DebugLog.Write("[Hook] ClickClick: cancelled by right-click");
            DragCancelled?.Invoke(this, EventArgs.Empty);
            return (IntPtr)1;
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void EmitDragUpdated(int x, int y)
    {
        double rx = Math.Min(_dragStartPoint.X, x);
        double ry = Math.Min(_dragStartPoint.Y, y);
        double rw = Math.Abs(x - _dragStartPoint.X);
        double rh = Math.Abs(y - _dragStartPoint.Y);
        if (rw > 1 && rh > 1)
            DragUpdated?.Invoke(this, new DragRectEventArgs(new System.Windows.Rect(rx, ry, rw, rh), _dragStartPoint));
    }

    private void EmitDragCompleted(int x, int y)
    {
        double rx = Math.Min(_dragStartPoint.X, x);
        double ry = Math.Min(_dragStartPoint.Y, y);
        double rw = Math.Abs(x - _dragStartPoint.X);
        double rh = Math.Abs(y - _dragStartPoint.Y);
        if (rw > 1 && rh > 1)
        {
            var rect = new System.Windows.Rect(rx, ry, rw, rh);
            DragCompleted?.Invoke(this, new DragRectEventArgs(rect, _dragStartPoint));
            _hasPendingDrags = true;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int msg = wParam.ToInt32();
            var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (msg == WM_KEYDOWN && hs.vkCode == VK_ESCAPE)
            {
                short ctrlState = GetAsyncKeyState(VK_CONTROL);
                bool ctrlHeld = (ctrlState & 0x8000) != 0;

                // Cancel any in-progress drag (hold-drag or click-click)
                if (_isDragging)
                {
                    _isDragging = false;
                    DragCancelled?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }
                if (_isClickClickActive)
                {
                    _isClickClickActive = false;
                    DragCancelled?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }

                if (ctrlHeld && CanRestore)
                {
                    RestoreRequested?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }
                else if (!ctrlHeld)
                {
                    DismissRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (msg == WM_KEYUP)
            {
                if ((hs.vkCode == VK_CONTROL || hs.vkCode == VK_LCONTROL || hs.vkCode == VK_RCONTROL) && _hasPendingDrags)
                {
                    _hasPendingDrags = false;
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
