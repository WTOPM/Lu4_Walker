using System.IO.Ports;
using System.Text;

namespace WTOPMBot.Services.Input;

/// <summary>
/// All gameplay input goes through Teensy HID (keyboard + mouse) over Serial.
/// Ready only after a successful PING→PONG handshake (so COM1 won't look green).
/// </summary>
public sealed class TeensyKeyInput : IKeyInput
{
    private readonly object _gate = new();
    private SerialPort? _port;
    private bool _handshakeOk;

    public string Name => string.IsNullOrWhiteSpace(PortName) ? "Teensy (нет COM)" : $"Teensy ({PortName})";
    public string PortName { get; }
    public bool IsReady => _port is { IsOpen: true } && _handshakeOk;
    public string? LastError { get; private set; }

    public TeensyKeyInput(string portName)
    {
        PortName = portName?.Trim() ?? string.Empty;
        TryOpen();
    }

    /// <summary>Probe ports quickly; returns first that answers PONG.</summary>
    public static string? FindLivePort(IEnumerable<string> ports, int timeoutMs = 700)
    {
        foreach (var port in ports
                     .OrderBy(p => string.Equals(p, "COM1", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                     .ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (ProbePing(port, timeoutMs))
                return port;
        }

        return null;
    }

    public static bool ProbePing(string portName, int timeoutMs = 700)
    {
        if (string.IsNullOrWhiteSpace(portName))
            return false;

        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName.Trim(), 115200)
            {
                NewLine = "\n",
                Encoding = Encoding.ASCII,
                ReadTimeout = timeoutMs,
                WriteTimeout = 400,
                DtrEnable = true,
                RtsEnable = true
            };
            port.Open();
            Thread.Sleep(200);
            while (port.BytesToRead > 0)
                port.ReadExisting();

            port.Write("PING\n");
            var deadline = Environment.TickCount64 + timeoutMs;
            var buf = new StringBuilder();
            while (Environment.TickCount64 < deadline)
            {
                while (port.BytesToRead > 0)
                    buf.Append(port.ReadExisting());
                if (buf.ToString().Contains("PONG", StringComparison.OrdinalIgnoreCase))
                    return true;
                Thread.Sleep(20);
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (port is { IsOpen: true })
                    port.Close();
            }
            catch { /* ignore */ }

            port?.Dispose();
        }
    }

    public void Tap(byte virtualKey, nint targetHwnd, int? holdMs = null)
    {
        _ = targetHwnd;
        var hold = holdMs ?? HumanInput.NextKeyHoldMs();
        SendAndWait($"TAP {virtualKey:X2} {hold}", expectOk: true);
    }

    public void ClickMouse(bool leftButton, nint targetHwnd, int? holdMs = null)
    {
        _ = targetHwnd;
        var hold = holdMs ?? Random.Shared.Next(35, 95);
        var side = leftButton ? "L" : "R";
        SendAndWait($"CLICK {side} {hold}", expectOk: true);
    }

    public void NudgeMouse(int dx, int dy, nint targetHwnd)
    {
        _ = targetHwnd;
        dx = Math.Clamp(dx, -40, 40);
        dy = Math.Clamp(dy, -40, 40);
        if (dx == 0 && dy == 0)
            return;
        SendAndWait($"MOVE {dx} {dy}", expectOk: true);
    }

    private void SendAndWait(string command, bool expectOk)
    {
        lock (_gate)
        {
            LastError = null;
            if (!IsReady && !TryOpen())
            {
                LastError ??= $"Teensy COM «{PortName}» недоступен";
                return;
            }

            try
            {
                while (_port!.BytesToRead > 0)
                    _port.ReadExisting();

                var pre = HumanInput.NextPrePressDelayMs();
                if (pre > 0)
                    Thread.Sleep(pre);

                _port.Write(command + "\n");

                var deadline = Environment.TickCount64 + 1200;
                var buf = new StringBuilder();
                while (Environment.TickCount64 < deadline)
                {
                    while (_port.BytesToRead > 0)
                        buf.Append(_port.ReadExisting());

                    var text = buf.ToString();
                    if (expectOk && text.Contains("OK", StringComparison.Ordinal))
                        return;
                    if (text.Contains("ERR", StringComparison.Ordinal))
                    {
                        LastError = text.Trim();
                        return;
                    }

                    Thread.Sleep(8);
                }

                LastError = "Teensy не ответил OK";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _handshakeOk = false;
                try { _port?.Close(); } catch { /* ignore */ }
                _port = null;
            }
        }
    }

    private bool TryOpen()
    {
        _handshakeOk = false;

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
                WriteTimeout = 800,
                DtrEnable = true,
                RtsEnable = true
            };
            _port.Open();
            Thread.Sleep(250);
            while (_port.BytesToRead > 0)
                _port.ReadExisting();

            if (!HandshakePing())
            {
                LastError = $"На {PortName} нет Teensy (нет PONG). Выбери COM3";
                try { _port.Close(); } catch { /* ignore */ }
                _port.Dispose();
                _port = null;
                return false;
            }

            LastError = null;
            _handshakeOk = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _port = null;
            _handshakeOk = false;
            return false;
        }
    }

    private bool HandshakePing()
    {
        if (_port is not { IsOpen: true })
            return false;

        try
        {
            while (_port.BytesToRead > 0)
                _port.ReadExisting();

            _port.Write("PING\n");
            var deadline = Environment.TickCount64 + 900;
            var buf = new StringBuilder();
            while (Environment.TickCount64 < deadline)
            {
                while (_port.BytesToRead > 0)
                    buf.Append(_port.ReadExisting());
                if (buf.ToString().Contains("PONG", StringComparison.OrdinalIgnoreCase))
                    return true;
                Thread.Sleep(20);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _handshakeOk = false;
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
}
