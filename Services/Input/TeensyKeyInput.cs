using System.IO.Ports;
using System.Text;

namespace WTOPMBot.Services.Input;

/// <summary>
/// Teensy 4.x HID keyboard via Serial. Not used in UI until hardware is connected.
/// </summary>
public sealed class TeensyKeyInput : IKeyInput
{
    private SerialPort? _port;

    public string Name => string.IsNullOrWhiteSpace(PortName) ? "Teensy (нет COM)" : $"Teensy ({PortName})";
    public string PortName { get; }
    public bool IsReady => _port is { IsOpen: true };
    public string? LastError { get; private set; }

    public TeensyKeyInput(string portName)
    {
        PortName = portName?.Trim() ?? string.Empty;
        TryOpen();
    }

    public void Tap(byte virtualKey, nint targetHwnd)
    {
        _ = targetHwnd;
        LastError = null;
        if (!IsReady && !TryOpen())
        {
            LastError ??= $"Teensy COM «{PortName}» недоступен";
            return;
        }

        try
        {
            _port!.Write($"TAP {virtualKey:X2}\n");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            try { _port?.Close(); } catch { /* ignore */ }
        }
    }

    private bool TryOpen()
    {
        if (string.IsNullOrWhiteSpace(PortName))
        {
            LastError = "Укажите COM-порт Teensy";
            return false;
        }

        try
        {
            _port?.Dispose();
            _port = new SerialPort(PortName, 115200)
            {
                NewLine = "\n",
                Encoding = Encoding.ASCII,
                ReadTimeout = 200,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };
            _port.Open();
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _port = null;
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_port is { IsOpen: true })
                _port.Close();
        }
        catch
        {
            // ignore
        }

        _port?.Dispose();
        _port = null;
    }
}
