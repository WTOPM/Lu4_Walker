using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WTOPMBot.Services;

/// <summary>
/// Global hotkeys that work even when another app (the game) has focus.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    public const int IdStart = 1;
    public const int IdStop = 2;
    public const int IdScreenshot = 3;

    private const int WmHotkey = 0x0312;
    private const uint ModNorepeat = 0x4000;

    private const uint VkPrior = 0x21;   // Page Up
    private const uint VkNext = 0x22;    // Page Down
    private const uint VkInsert = 0x2D;  // Insert

    private readonly Window _window;
    private HwndSource? _source;
    private nint _hwnd;
    private bool _pageUpRegistered;
    private bool _pageDownRegistered;
    private bool _insertRegistered;
    private bool _disposed;

    public event Action? StartPressed;
    public event Action? StopPressed;
    public event Action? ScreenshotPressed;

    public bool PageUpOk => _pageUpRegistered;
    public bool PageDownOk => _pageDownRegistered;
    public bool InsertOk => _insertRegistered;

    public GlobalHotkeys(Window window)
    {
        _window = window;
    }

    public void Attach()
    {
        var helper = new WindowInteropHelper(_window);
        _hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd)
                  ?? throw new InvalidOperationException("Не удалось получить HwndSource.");
        _source.AddHook(WndProc);

        _pageUpRegistered = RegisterHotKey(_hwnd, IdStart, ModNorepeat, VkPrior);
        _pageDownRegistered = RegisterHotKey(_hwnd, IdStop, ModNorepeat, VkNext);
        _insertRegistered = RegisterHotKey(_hwnd, IdScreenshot, ModNorepeat, VkInsert);
    }

    public string DescribeStatus()
    {
        var parts = new List<string>
        {
            PageUpOk ? "PgUp=Старт" : "PgUp недоступна",
            PageDownOk ? "PgDn=Стоп" : "PgDn недоступна",
            InsertOk ? "Ins=Скрин" : "Ins недоступна"
        };
        return string.Join(" · ", parts);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            switch (wParam.ToInt32())
            {
                case IdStart:
                    StartPressed?.Invoke();
                    handled = true;
                    break;
                case IdStop:
                    StopPressed?.Invoke();
                    handled = true;
                    break;
                case IdScreenshot:
                    ScreenshotPressed?.Invoke();
                    handled = true;
                    break;
            }
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_hwnd != nint.Zero)
        {
            if (_pageUpRegistered) UnregisterHotKey(_hwnd, IdStart);
            if (_pageDownRegistered) UnregisterHotKey(_hwnd, IdStop);
            if (_insertRegistered) UnregisterHotKey(_hwnd, IdScreenshot);
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
