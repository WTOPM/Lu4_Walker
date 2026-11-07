using System.Runtime.InteropServices;

namespace WTOPMBot.Services.Input;

/// <summary>
/// Sends a key to a specific game HWND via PostMessage (no focus steal).
/// Works for many games that still read the Win32 message queue.
/// </summary>
public sealed class SendInputKeyInput : IKeyInput
{
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const uint MapvkVkToVsc = 0;

    private readonly object _gate = new();

    public string Name => "PostMessage HWND";
    public bool IsReady => true;
    public string? LastError { get; private set; }

    public void Tap(byte virtualKey, nint targetHwnd)
    {
        lock (_gate)
        {
            LastError = null;

            if (targetHwnd == nint.Zero || !IsWindow(targetHwnd))
            {
                LastError = "HWND окна игры недоступен";
                return;
            }

            var scan = MapVirtualKey(virtualKey, MapvkVkToVsc);
            if (scan == 0)
            {
                LastError = $"Нет scan code для VK 0x{virtualKey:X2}";
                return;
            }

            // lParam: repeat=1, scan in bits 16-23
            var downLParam = (nint)(1u | (scan << 16));
            var upLParam = (nint)(1u | (scan << 16) | (1u << 30) | (1u << 31));

            if (!PostMessage(targetHwnd, WmKeydown, virtualKey, downLParam))
            {
                LastError = $"PostMessage KEYDOWN failed ({Marshal.GetLastWin32Error()})";
                return;
            }

            Thread.Sleep(Random.Shared.Next(40, 70));

            if (!PostMessage(targetHwnd, WmKeyup, virtualKey, upLParam))
                LastError = $"PostMessage KEYUP failed ({Marshal.GetLastWin32Error()})";
        }
    }

    public void Dispose()
    {
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
