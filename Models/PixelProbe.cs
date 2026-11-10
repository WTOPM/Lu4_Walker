namespace WTOPMBot.Models;

public sealed class PixelProbe
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Top-left at profile ReferenceWidth×Height.</summary>
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>Region size; 1×1 = single pixel.</summary>
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public int Tolerance { get; set; } = 35;

    /// <summary>How many matching pixels required in the region.</summary>
    public int MinMatches { get; set; } = 1;
}

public sealed class PixelProbeReading
{
    public string Id { get; init; } = string.Empty;
    public bool Matched { get; init; }
    public int MatchCount { get; init; }
    public byte SampleR { get; init; }
    public byte SampleG { get; init; }
    public byte SampleB { get; init; }
}

public sealed class CombatState
{
    public bool TargetSelected { get; init; }
    public bool DeathPixel { get; init; }
    public bool HasTargetHp { get; init; }
    public StatusBarReading? TargetHp { get; init; }

    /// <summary>Skull detected in target portrait and no HP fill.</summary>
    public bool TargetDead => DeathPixel;

    /// <summary>Alive = visible red fill on the target HP bar.</summary>
    public bool TargetAlive => HasTargetHp && !DeathPixel;

    public BotAction SuggestedAction =>
        TargetDead ? BotAction.Loot :
        TargetAlive ? BotAction.Attack :
        BotAction.SearchMob;

    public string Summary => SuggestedAction switch
    {
        BotAction.Loot => "Цель мертва — лут",
        BotAction.Attack => "Цель есть — атакуем",
        _ => "Цели нет — ищем"
    };

    public static CombatState FromTargetHp(StatusBarReading? targetHp, bool deathDetected = false)
    {
        // Only a real HP fill counts as alive. Empty trough alone is "no target".
        var hasHp = targetHp is { Detected: true, FillPercent: > 0 };
        // Skull + no HP fill = dead corpse still selected.
        var dead = deathDetected && !hasHp;
        return new CombatState
        {
            TargetSelected = hasHp || dead,
            DeathPixel = dead,
            HasTargetHp = hasHp,
            TargetHp = targetHp
        };
    }
}
