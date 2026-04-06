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
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int XBUTTON1 = 0x0001;
    private const int XBUTTON2 = 0x0002;
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
    public event EventHandler<ArrowLineEventArgs>? ArrowDragCompleted;
    public event EventHandler<ArrowLineEventArgs>? ArrowDragUpdated;
    public event EventHandler<DragRectEventArgs>? BoxDragCompleted;
    public event EventHandler<DragRectEventArgs>? BoxDragUpdated;
    public event EventHandler<DragRectEventArgs>? HighlightDragCompleted;
    public event EventHandler<DragRectEventArgs>? HighlightDragUpdated;
    public event EventHandler<StepsPlacedEventArgs>? StepsPlaced;
    public event EventHandler<StepsPlacedEventArgs>? StepsDragUpdated;
    public event EventHandler? DragCancelled;
    public event EventHandler? CtrlReleased;
    public event EventHandler? CtrlPressed;
    public event EventHandler? RestoreRequested;
    public event EventHandler? DismissRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? ToggleToolRequested;
    #endregion

    public bool IsEnabled { get; set; }
    public bool CanRestore { get; set; }
    public DragStyle DragStyle { get; set; }
    public ToolType ActiveTool { get; set; } = ToolType.Spotlight;
    public StepsShape StepsShape { get; set; } = StepsShape.Teardrop;
    public ModifierKey ActivationModifier { get; set; } = ModifierKey.Ctrl;
    public int ActivationKey { get; set; } = 0; // 0 = no key, modifier-only
    public ModifierKey ToggleModifier { get; set; } = ModifierKey.CtrlShift;
    public int ToggleKey { get; set; } = 0x51; // VK_Q
    public ModifierKey ToggleToolModifier { get; set; } = ModifierKey.CtrlShift;
    public int ToggleToolKey { get; set; } = 0x02; // VK_RBUTTON
    public bool IsRecordingHotkey { get; set; }
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
        bool modsHeld = ActivationModifier switch
        {
            ModifierKey.Ctrl => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            ModifierKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            ModifierKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlShift => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                  && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlAlt => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                 && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            ModifierKey.None => true, // No modifier required
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        };
        if (!modsHeld) return false;
        // If an activation key is configured and it's NOT a mouse button, it must also be held
        // Mouse button keys are checked via WM messages in the mouse hook, not via GetAsyncKeyState
        if (ActivationKey != 0 && !IsMouseButtonVk(ActivationKey)
            && (GetAsyncKeyState(ActivationKey) & 0x8000) == 0)
            return false;
        return true;
    }

    /// <summary>Checks if the given vkCode is one of the keys for the configured modifier.</summary>
    private bool IsActivationModifierVk(uint vkCode)
    {
        // Check if it's the activation key itself (but not mouse buttons — those are handled in mouse hook)
        if (ActivationKey != 0 && !IsMouseButtonVk(ActivationKey) && vkCode == (uint)ActivationKey)
            return true;
        return ActivationModifier switch
        {
            ModifierKey.Ctrl => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL,
            ModifierKey.Alt => vkCode is VK_MENU or VK_LMENU or VK_RMENU,
            ModifierKey.Shift => vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
            ModifierKey.CtrlShift => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
                                  or VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
            ModifierKey.CtrlAlt => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
                                 or VK_MENU or VK_LMENU or VK_RMENU,
            ModifierKey.None => false, // No keyboard modifier — activation is mouse-only
            _ => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
        };
    }

    /// <summary>For multi-key modifiers (CtrlShift, CtrlAlt), checks if ALL required keys are released.</summary>
    private bool IsActivationModifierFullyReleased()
    {
        // If an activation key is set and it's NOT a mouse button and it's released, the combo is broken
        if (ActivationKey != 0 && !IsMouseButtonVk(ActivationKey)
            && (GetAsyncKeyState(ActivationKey) & 0x8000) == 0)
            return true;
        return ActivationModifier switch
        {
            ModifierKey.Ctrl => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0,
            ModifierKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) == 0,
            ModifierKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0,
            ModifierKey.CtrlShift => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0
                                  || (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0,
            ModifierKey.CtrlAlt => (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0
                                 || (GetAsyncKeyState(VK_MENU) & 0x8000) == 0,
            ModifierKey.None => true, // No modifier — always "released" (mouse button up handles this)
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
            ModifierKey.None => true, // No modifier required
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        };
    }

    /// <summary>Checks if the toggle-tool modifier key(s) are currently held.</summary>
    private bool IsToggleToolModifierHeld()
    {
        return ToggleToolModifier switch
        {
            ModifierKey.Ctrl => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            ModifierKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            ModifierKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlShift => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                  && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            ModifierKey.CtrlAlt => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                                 && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            ModifierKey.None => true,
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        };
    }

    /// <summary>Checks if the given WM message + mouseData matches the configured toggle-tool mouse button.</summary>
    private bool IsToggleToolMouseButtonMsg(int msg, uint mouseData)
    {
        if (!IsMouseButtonVk(ToggleToolKey)) return false;
        int expectedDown = MouseVkToDownMsg(ToggleToolKey);
        if (msg != expectedDown) return false;
        return IsXButtonMatch(ToggleToolKey, mouseData);
    }

    /// <summary>Returns true if the VK code is a configurable mouse button (middle, X1, X2). Left/right are not configurable.</summary>
    private static bool IsMouseButtonVk(int vk) => vk is 0x04 or 0x05 or 0x06;

    /// <summary>Returns the WM_*BUTTONDOWN message for the given mouse button VK, or 0 if not a configurable mouse button.</summary>
    private static int MouseVkToDownMsg(int vk) => vk switch
    {
        0x04 => WM_MBUTTONDOWN,
        0x05 => WM_XBUTTONDOWN,
        0x06 => WM_XBUTTONDOWN,
        _ => 0
    };

    /// <summary>Returns the WM_*BUTTONUP message for the given mouse button VK, or 0 if not a configurable mouse button.</summary>
    private static int MouseVkToUpMsg(int vk) => vk switch
    {
        0x04 => WM_MBUTTONUP,
        0x05 => WM_XBUTTONUP,
        0x06 => WM_XBUTTONUP,
        _ => 0
    };

    /// <summary>Checks if an XBUTTON message matches the expected VK (X1 vs X2) by inspecting mouseData high word.</summary>
    private static bool IsXButtonMatch(int vk, uint mouseData)
    {
        int xButton = (int)(mouseData >> 16);
        return vk switch
        {
            0x05 => xButton == XBUTTON1,
            0x06 => xButton == XBUTTON2,
            _ => true // middle click doesn't need xButton check
        };
    }

    /// <summary>Checks if the given WM message + mouseData matches the configured activation mouse button.</summary>
    private bool IsActivationMouseButtonMsg(int msg, uint mouseData)
    {
        if (!IsMouseButtonVk(ActivationKey)) return false;
        int expectedDown = MouseVkToDownMsg(ActivationKey);
        if (msg != expectedDown) return false;
        return IsXButtonMatch(ActivationKey, mouseData);
    }

    /// <summary>Checks if the given WM message + mouseData is the UP event for the configured activation mouse button.</summary>
    private bool IsActivationMouseButtonUpMsg(int msg, uint mouseData)
    {
        if (!IsMouseButtonVk(ActivationKey)) return false;
        int expectedUp = MouseVkToUpMsg(ActivationKey);
        if (msg != expectedUp) return false;
        return IsXButtonMatch(ActivationKey, mouseData);
    }

    /// <summary>Checks if the given WM message + mouseData matches the configured toggle mouse button.</summary>
    private bool IsToggleMouseButtonMsg(int msg, uint mouseData)
    {
        if (!IsMouseButtonVk(ToggleKey)) return false;
        int expectedDown = MouseVkToDownMsg(ToggleKey);
        if (msg != expectedDown) return false;
        return IsXButtonMatch(ToggleKey, mouseData);
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

        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            // Toggle via mouse button (works regardless of IsEnabled, like keyboard toggle)
            // Skip when recording hotkeys in settings
            if (!IsRecordingHotkey && IsMouseButtonVk(ToggleKey)
                && IsToggleMouseButtonMsg(msg, hs.mouseData) && IsToggleModifierHeld())
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1;
            }

            // Toggle tool via mouse button
            if (!IsRecordingHotkey && IsMouseButtonVk(ToggleToolKey)
                && IsToggleToolMouseButtonMsg(msg, hs.mouseData) && IsToggleToolModifierHeld())
            {
                // Cancel any in-progress drag so a stray left-click doesn't become a spotlight
                _isDragging = false;
                _isClickClickActive = false;
                _hasPendingDrags = false;
                ToggleToolRequested?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1;
            }
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

    /// <summary>Hold-drag mode: activation+click down, drag, release = cutout.
    /// When ActivationKey is a mouse button, that button triggers the drag instead of left click.</summary>
    private IntPtr HandleHoldDragMouse(int msg, IntPtr lParam, int nCode, IntPtr wParam)
    {
        var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        bool isMouseButtonActivation = IsMouseButtonVk(ActivationKey);

        // Determine if this message is the "activation down" event
        bool isActivationDown = isMouseButtonActivation
            ? IsActivationMouseButtonMsg(msg, hs.mouseData)
            : msg == WM_LBUTTONDOWN;

        // Determine if this message is the "activation up" event
        bool isActivationUp = isMouseButtonActivation
            ? IsActivationMouseButtonUpMsg(msg, hs.mouseData)
            : msg == WM_LBUTTONUP;

        if (isActivationDown && !_isDragging)
        {
            if (IsActivationModifierHeld())
            {
                // Circle mode: place immediately on click, no drag needed
                if (ActiveTool == ToolType.Steps && StepsShape == StepsShape.Circle)
                {
                    var pt = new System.Windows.Point(hs.pt.x, hs.pt.y);
                    StepsPlaced?.Invoke(this, new StepsPlacedEventArgs(pt, pt));
                    _hasPendingDrags = true;
                    if (isMouseButtonActivation && ActivationModifier == ModifierKey.None)
                    {
                        _hasPendingDrags = false;
                        CtrlReleased?.Invoke(this, EventArgs.Empty);
                    }
                    return (IntPtr)1;
                }

                _isDragging = true;
                _dragStartPoint = new System.Windows.Point(hs.pt.x, hs.pt.y);                SpotlightOverlay.DebugLog.Write($"[Hook] HoldDrag: start at {hs.pt.x},{hs.pt.y}");
                // Fire CtrlPressed for mouse-button activation so screenshot pre-capture works
                if (isMouseButtonActivation)
                    CtrlPressed?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1;
            }
        }
        else if (isActivationUp && _isDragging)
        {
            _isDragging = false;
            SpotlightOverlay.DebugLog.Write($"[Hook] HoldDrag: end at {hs.pt.x},{hs.pt.y}");
            EmitDragCompleted(hs.pt.x, hs.pt.y);

            // For mouse-button-only activation (no keyboard modifier), apply immediately
            // since there's no modifier key-up event to trigger batch apply
            if (isMouseButtonActivation && ActivationModifier == ModifierKey.None)
            {
                _hasPendingDrags = false;
                CtrlReleased?.Invoke(this, EventArgs.Empty);
            }
            return (IntPtr)1;
        }
        else if (msg == WM_MOUSEMOVE && _isDragging)
        {
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

    /// <summary>Click-click mode: activation+click = start, move, click = end. Right-click cancels.
    /// When ActivationKey is a mouse button, that button triggers instead of left click.</summary>
    private IntPtr HandleClickClickMouse(int msg, IntPtr lParam, int nCode, IntPtr wParam)
    {
        var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        bool isMouseButtonActivation = IsMouseButtonVk(ActivationKey);

        // Determine if this message is the "activation down" event
        bool isActivationDown = isMouseButtonActivation
            ? IsActivationMouseButtonMsg(msg, hs.mouseData)
            : msg == WM_LBUTTONDOWN;

        if (isActivationDown)
        {
            if (_isClickClickActive)
            {
                // Second click — complete the cutout
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
                    // Circle mode: place immediately on first click, no second click needed
                    if (ActiveTool == ToolType.Steps && StepsShape == StepsShape.Circle)
                    {
                        var pt = new System.Windows.Point(hs.pt.x, hs.pt.y);
                        StepsPlaced?.Invoke(this, new StepsPlacedEventArgs(pt, pt));
                        _hasPendingDrags = true;
                        if (!IsActivationModifierHeld())
                        {
                            _hasPendingDrags = false;
                            CtrlReleased?.Invoke(this, EventArgs.Empty);
                        }
                        return (IntPtr)1;
                    }

                    // First modifier+click — set start point
                    _isClickClickActive = true;
                    _dragStartPoint = new System.Windows.Point(hs.pt.x, hs.pt.y);
                    SpotlightOverlay.DebugLog.Write($"[Hook] ClickClick: first click at {hs.pt.x},{hs.pt.y}");
                    // Fire CtrlPressed for mouse-button activation so screenshot pre-capture works
                    if (isMouseButtonActivation)
                        CtrlPressed?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }
            }
        }
        else if (msg == WM_MOUSEMOVE && _isClickClickActive)
        {
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
        if (ActiveTool == ToolType.Arrow)
        {
            var current = new System.Windows.Point(x, y);
            ArrowDragUpdated?.Invoke(this, new ArrowLineEventArgs(_dragStartPoint, current));
        }
        else if (ActiveTool == ToolType.Box)
        {
            double rx = Math.Min(_dragStartPoint.X, x);
            double ry = Math.Min(_dragStartPoint.Y, y);
            double rw = Math.Abs(x - _dragStartPoint.X);
            double rh = Math.Abs(y - _dragStartPoint.Y);
            if (rw > 1 && rh > 1)
                BoxDragUpdated?.Invoke(this, new DragRectEventArgs(new System.Windows.Rect(rx, ry, rw, rh), _dragStartPoint));
        }
        else if (ActiveTool == ToolType.Highlight)
        {
            double rx = Math.Min(_dragStartPoint.X, x);
            double ry = Math.Min(_dragStartPoint.Y, y);
            double rw = Math.Abs(x - _dragStartPoint.X);
            double rh = Math.Abs(y - _dragStartPoint.Y);
            if (rw > 1 && rh > 1)
                HighlightDragUpdated?.Invoke(this, new DragRectEventArgs(new System.Windows.Rect(rx, ry, rw, rh), _dragStartPoint));
        }
        else if (ActiveTool == ToolType.Steps)
        {
            var current = new System.Windows.Point(x, y);
            StepsDragUpdated?.Invoke(this, new StepsPlacedEventArgs(_dragStartPoint, current, IsActivationModifierHeld()));
        }
        else
        {
            double rx = Math.Min(_dragStartPoint.X, x);
            double ry = Math.Min(_dragStartPoint.Y, y);
            double rw = Math.Abs(x - _dragStartPoint.X);
            double rh = Math.Abs(y - _dragStartPoint.Y);
            if (rw > 1 && rh > 1)
                DragUpdated?.Invoke(this, new DragRectEventArgs(new System.Windows.Rect(rx, ry, rw, rh), _dragStartPoint));
        }
    }

    private void EmitDragCompleted(int x, int y)
    {
        if (ActiveTool == ToolType.Arrow)
        {
            var endPoint = new System.Windows.Point(x, y);
            ArrowDragCompleted?.Invoke(this, new ArrowLineEventArgs(_dragStartPoint, endPoint));
            _hasPendingDrags = true;
        }
        else if (ActiveTool == ToolType.Box)
        {
            double rx = Math.Min(_dragStartPoint.X, x);
            double ry = Math.Min(_dragStartPoint.Y, y);
            double rw = Math.Abs(x - _dragStartPoint.X);
            double rh = Math.Abs(y - _dragStartPoint.Y);
            if (rw > 1 && rh > 1)
            {
                var rect = new System.Windows.Rect(rx, ry, rw, rh);
                BoxDragCompleted?.Invoke(this, new DragRectEventArgs(rect, _dragStartPoint));
                _hasPendingDrags = true;
            }
        }
        else if (ActiveTool == ToolType.Highlight)
        {
            double rx = Math.Min(_dragStartPoint.X, x);
            double ry = Math.Min(_dragStartPoint.Y, y);
            double rw = Math.Abs(x - _dragStartPoint.X);
            double rh = Math.Abs(y - _dragStartPoint.Y);
            if (rw > 1 && rh > 1)
            {
                var rect = new System.Windows.Rect(rx, ry, rw, rh);
                HighlightDragCompleted?.Invoke(this, new DragRectEventArgs(rect, _dragStartPoint));
                _hasPendingDrags = true;
            }
        }
        else if (ActiveTool == ToolType.Steps)
        {
            var releasePoint = new System.Windows.Point(x, y);
            StepsPlaced?.Invoke(this, new StepsPlacedEventArgs(_dragStartPoint, releasePoint, IsActivationModifierHeld()));
            _hasPendingDrags = true;
        }
        else
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
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

            // Toggle hotkey works regardless of IsEnabled so user can re-enable
            // But skip when the settings window is recording a new hotkey
            // Also skip if ToggleKey is a mouse button — that's handled in MouseHookCallback
            if (isKeyDown && !IsRecordingHotkey && !IsMouseButtonVk(ToggleKey)
                && hs.vkCode == (uint)ToggleKey && IsToggleModifierHeld())
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1; // eat the key
            }

            // Toggle tool hotkey — skip if ToggleToolKey is a mouse button
            if (isKeyDown && !IsRecordingHotkey && !IsMouseButtonVk(ToggleToolKey)
                && hs.vkCode == (uint)ToggleToolKey && IsToggleToolModifierHeld())
            {
                ToggleToolRequested?.Invoke(this, EventArgs.Empty);
                return (IntPtr)1;
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
                // When recording a hotkey in settings, let Escape pass through to WPF
                if (IsRecordingHotkey)
                {
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

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
                _hasPendingDrags = false;
                CtrlReleased?.Invoke(this, EventArgs.Empty);
            }            else if (isKeyDown && IsActivationModifierVk(hs.vkCode)
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
