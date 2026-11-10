namespace WTOPMBot.Models;

public sealed class KeyBindSaveData
{
    public KeyBindProfile Profile { get; init; } = KeyBindProfile.CreateDefault();
    public int NextTargetAttempts { get; init; } = 3;
    public bool ApproachNextTargetEnabled { get; init; }
    public int ApproachNextTargetIntervalMs { get; init; } = 1500;
    public int LootAttempts { get; init; } = 3;
    public int LootIntervalMs { get; init; } = 800;
}
