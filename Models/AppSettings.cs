namespace WTOPMBot.Models;

public sealed class AppSettings
{
    public string? PreferredProcessName { get; set; }
    public string? PreferredWindowTitle { get; set; }
    public int SampleX { get; set; }
    public int SampleY { get; set; }
    public bool AlwaysOnTop { get; set; }

    /// <summary>SendInput or Teensy</summary>
    public string InputBackend { get; set; } = "SendInput";
    public string TeensyComPort { get; set; } = "COM3";
    public bool AutoAttackEnabled { get; set; } = true;
}
