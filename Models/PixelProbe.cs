namespace WTOPMBot.Models;

public sealed class PixelProbe
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public int Tolerance { get; set; } = 35;
}

public sealed class PixelProbeReading
{
    public string Id { get; init; } = string.Empty;
    public bool Matched { get; init; }
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

    /// <summary>Dead = selected + no HP + death pixel.</summary>
    public bool TargetDead => TargetSelected && !HasTargetHp && DeathPixel;

    /// <summary>Alive = selected pixel + HP bar only (death pixel ignored).</summary>
    public bool TargetAlive => TargetSelected && HasTargetHp;

    public byte SuggestedKey
    {
        get
        {
            if (!TargetSelected)
                return 0x33; // '3'
            if (TargetAlive)
                return 0x31; // '1' — цель + HP
            if (TargetDead)
                return 0x32; // '2'
            return 0;
        }
    }

    public string SuggestedKeyLabel => SuggestedKey switch
    {
        0x31 => "1",
        0x32 => "2",
        0x33 => "3",
        _ => "—"
    };

    public string Summary
    {
        get
        {
            if (!TargetSelected)
                return "нет цели→3";
            if (TargetAlive)
                return "жива→1";
            if (TargetDead)
                return "мертв→2";
            return "неясно";
        }
    }
}
