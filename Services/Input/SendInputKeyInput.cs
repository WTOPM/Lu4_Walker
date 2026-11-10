using System.Runtime.InteropServices;

namespace WTOPMBot.Services.Input;

/// <summary>
/// Real keyboard injection via Win32 SendInput.
/// Works while LMB is held (camera / click-to-move) — PostMessage often does not.
/// Game window should be focused (normal when the player holds the mouse in-client).
/// </summary>
public sealed class SendInputKeyInput : IKeyInput
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfLeftDown = 0x0002;
    private const uint MouseeventfLeftUp = 0x0004;
    private const uint MouseeventfRightDown = 0x0008;
    private const uint MouseeventfRightUp = 0x0010;
    private const uint MapvkVkToVsc = 0;

    private readonly object _gate = new();

    public string Name => "SendInput";
    public bool IsReady => true;
    public string? LastError { get; private set; }

    public void Tap(byte virtualKey, nint targetHwnd, int? holdMs = null)
    {
        lock (_gate)
        {
            LastError = null;

            if (targetHwnd != nint.Zero && !IsWindow(targetHwnd))
            {
                LastError = "HWND окна игры недоступен";
                return;
            }

            if (targetHwnd != nint.Zero)
                EnsureGameCanReceiveKeys(targetHwnd);

            var scan = (ushort)MapVirtualKey(virtualKey, MapvkVkToVsc);
            if (scan == 0)
            {
                LastError = $"Нет scan code для VK 0x{virtualKey:X2}";
                return;
            }

            var pre = HumanInput.NextPrePressDelayMs();
            if (pre > 0)
                Thread.Sleep(pre);

            if (!SendKey(scan, keyUp: false))
            {
                LastError = $"SendInput KEYDOWN failed ({Marshal.GetLastWin32Error()})";
                return;
            }

            Thread.Sleep(holdMs ?? HumanInput.NextKeyHoldMs());

            if (!SendKey(scan, keyUp: true))
                LastError = $"SendInput KEYUP failed ({Marshal.GetLastWin32Error()})";
        }
    }

    public void ClickMouse(bool leftButton, nint targetHwnd, int? holdMs = null)
    {
        lock (_gate)
        {
            LastError = null;
            if (targetHwnd != nint.Zero)
                EnsureGameCanReceiveKeys(targetHwnd);

            var down = leftButton ? MouseeventfLeftDown : MouseeventfRightDown;
            var up = leftButton ? MouseeventfLeftUp : MouseeventfRightUp;
            if (!SendMouse(0, 0, down))
            {
                LastError = "SendInput mouse down failed";
                return;
            }

            Thread.Sleep(holdMs ?? Random.Shared.Next(35, 95));
            if (!SendMouse(0, 0, up))
                LastError = "SendInput mouse up failed";
        }
    }

    public void NudgeMouse(int dx, int dy, nint targetHwnd)
    {
        lock (_gate)
        {
            LastError = null;
            if (targetHwnd != nint.Zero)
                EnsureGameCanReceiveKeys(targetHwnd);
            SendMouse(dx, dy, MouseeventfMove);
        }
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Soft focus restore without synthesizing mouse-up — LMB stay held.
    /// </summary>
    private static void EnsureGameCanReceiveKeys(nint hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
            return;

        // If a child of the game is focused, still OK.
        if (foreground != nint.Zero && IsChild(hwnd, foreground))
            return;

        // Attach to foreground thread so SetForegroundWindow is allowed, then restore.
        var fgThread = GetWindowThreadProcessId(foreground, out _);
        var thisThread = GetCurrentThreadId();
        var attached = false;
        if (fgThread != 0 && fgThread != thisThread)
            attached = AttachThreadInput(thisThread, fgThread, true);

        try
        {
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(thisThread, fgThread, false);
        }
    }

    private static bool SendKey(ushort scan, bool keyUp)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = KeyeventfScancode | (keyUp ? KeyeventfKeyup : 0),
                    time = 0,
                    dwExtraInfo = nint.Zero
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }

    private static bool SendMouse(int dx, int dy, uint flags)
    {
        var input = new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = nint.Zero
                }
            }
        };

        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    /// <summary>Must match native INPUT size (union of mouse/keyboard/hardware).</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsChild(nint hWndParent, nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
