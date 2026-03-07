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
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_MENU = 0x12;    // Alt
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_ESCAPE = 0x1B;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

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
    public event EventHandler? CtrlPressed;
    public event EventHandler? RestoreRequested;
    public event EventHandler? DismissRequested;
    public event EventHandler? ToggleRequested;
    #endregion

    public bool IsEnabled { get; set; }
    public bool CanRestore { get; set; }
    public DragStyle DragStyle { get; set; }
    public ModifierKey ActivationModifier { get; set; } = ModifierKey.Ctrl;
    public ModifierKey ToggleModifier { get; set; } = ModifierKey.CtrlShift;
    public int ToggleKey { get; set; } = 0x51; // VK_Q
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

    /// <summary>Checks if the configured activation modifier key(s) are currently held.</summary>
    private bool IsActivationModifierHeld()
    {
        return ActivationModifier switch
        {
            ModifierKey.Ctrl => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            ModifierKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            ModifierKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlShift => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                  && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlAlt => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                 && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        };
    }

    /// <summary>Checks if the given vkCode is one of the keys for the configured modifier.</summary>
    private bool IsActivationModifierVk(uint vkCode)
    {
        return ActivationModifier switch
        {
            ModifierKey.Ctrl => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL,
            ModifierKey.Alt => vkCode is VK_MENU or VK_LMENU or VK_RMENU,
            ModifierKey.Shift => vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
            ModifierKey.CtrlShift => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
                                  or VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
            ModifierKey.CtrlAlt => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
                                 or VK_MENU or VK_LMENU or VK_RMENU,
            _ => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
        };
    }

    /// <summary>For multi-key modifiers (CtrlShift, CtrlAlt), checks if ALL required keys are released.</summary>
    private bool IsActivationModifierFullyReleased()
    {
        return ActivationModifier switch
        {
            ModifierKey.Ctrl => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0,
            ModifierKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) == 0,
            ModifierKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0,
            ModifierKey.CtrlShift => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0
                                  || (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0,
            ModifierKey.CtrlAlt => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0
                                 || (GetAsyncKeyState(VK_MENU) & 0x8000) == 0,
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0
        };
    }

    /// <summary>Checks if the toggle modifier key(s) are currently held.</summary>
    private bool IsToggleModifierHeld()
    {
        return ToggleModifier switch
        {
            ModifierKey.Ctrl => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            ModifierKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            ModifierKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlShift => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                  && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlAlt => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                 && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        };
    }

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
        if (nCode >= 0)
        {
            // Suppress synthetic mouse events from DismissStartMenu (tagged via dwExtraInfo)
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (hs.dwExtraInfo == (IntPtr)0x534D4449)
                return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

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
            if (IsActivationModifierHeld())
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

                // If modifier is still held, respect batch mode (wait for release)
                // If modifier is NOT held, apply immediately
                if (!IsActivationModifierHeld())
                {
                    SpotlightOverlay.DebugLog.Write("[Hook] ClickClick: modifier not held, applying immediately");
                    _hasPendingDrags = false;
                    CtrlReleased?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SpotlightOverlay.DebugLog.Write("[Hook] ClickClick: modifier held, batching");
                }
                return (IntPtr)1;
            }
            else
            {
                if (IsActivationModifierHeld())
                {
                    // First modifier+click — set start point
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
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

            // Toggle hotkey works regardless of IsEnabled so user can re-enable
            if (isKeyDown && hs.vkCode == (uint)ToggleKey && IsToggleModifierHeld())
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1; // eat the key
            }
        }

        if (nCode >= 0 && IsEnabled)
        {
            int msg = wParam.ToInt32();
            var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown && hs.vkCode == VK_ESCAPE)
            {
                bool modifierHeld = IsActivationModifierHeld();

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

                if (modifierHeld && CanRestore)
                {
                    RestoreRequested?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }
                else if (!modifierHeld)
                {
                    DismissRequested?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }
                else if (modifierHeld && _hasPendingDrags)
                {
                    return (IntPtr)1;
                }
            }
            else if (isKeyUp && IsActivationModifierVk(hs.vkCode) && _hasPendingDrags)
            {
                // For single-key modifiers, the key-up event itself means it's released.
                // For combo modifiers, check if the other key in the combo is also released.
                bool released = ActivationModifier switch
                {
                    ModifierKey.Ctrl => true,  // key-up for Ctrl = released
                    ModifierKey.Alt => true,
                    ModifierKey.Shift => true,
                    // For combos: releasing either key breaks the combo
                    ModifierKey.CtrlShift => true,
                    ModifierKey.CtrlAlt => true,
                    _ => true
                };
                if (released)
                {
                    _hasPendingDrags = false;
                    CtrlReleased?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (isKeyDown && IsActivationModifierVk(hs.vkCode)
                     && !_isDragging && !_isClickClickActive)
            {
                // Only fire pressed when the full modifier combo is held
                if (IsActivationModifierHeld())
                    CtrlPressed?.Invoke(this, EventArgs.Empty);
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
