namespace WTOPMBot.Models;

public sealed class AppSettings
{
    public string? PreferredProcessName { get; set; }
    public string? PreferredWindowTitle { get; set; }
    public int SampleX { get; set; }
    public int SampleY { get; set; }
    public bool AlwaysOnTop { get; set; }

    /// <summary>Always Teensy for hardware HID keypresses.</summary>
    public string InputBackend { get; set; } = "Teensy";
    public string TeensyComPort { get; set; } = "";
    public bool AutoAttackEnabled { get; set; } = true;

    /// <summary>
    /// How many Next-target presses to try between named /target keys.
    /// </summary>
    public int NextTargetAttempts { get; set; } = 3;

    /// <summary>
    /// While running to a nick-target with full HP, periodically press Next target.
    /// </summary>
    public bool ApproachNextTargetEnabled { get; set; }

    /// <summary>Interval in ms for approach Next-target presses (e.g. 1500).</summary>
    public int ApproachNextTargetIntervalMs { get; set; } = 1500;

    /// <summary>How many Loot key presses while corpse skull is visible.</summary>
    public int LootAttempts { get; set; } = 3;

    /// <summary>Delay between Loot presses (ms).</summary>
    public int LootIntervalMs { get; set; } = 800;

    public KeyBindProfile KeyBinds { get; set; } = KeyBindProfile.CreateDefault();
}
