namespace WTOPMBot.Models;

public sealed class StatusBarProfile
{
    public int ReferenceWidth { get; set; } = 2560;
    public int ReferenceHeight { get; set; } = 1440;
    public bool AutoCalibrated { get; set; }
    public List<StatusBarDefinition> Bars { get; set; } = [];
    public List<PixelProbe> Probes { get; set; } = [];
}

public sealed class StatusBarDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute coords at <see cref="StatusBarProfile.ReferenceWidth"/>×Height (for readability).</summary>
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }

    /// <summary>Percent of client size (0–100). Preferred for sampling when set.</summary>
    public double X1Pct { get; set; }
    public double Y1Pct { get; set; }
    public double X2Pct { get; set; }
    public double Y2Pct { get; set; }

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public int Tolerance { get; set; } = 40;

    public bool HasPercents =>
        X2Pct > X1Pct && Y2Pct >= Y1Pct && X2Pct > 0 && Y2Pct >= 0;
}

public sealed class StatusBarReading
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double FillPercent { get; init; }
    public bool Detected { get; init; }
    public byte SampleR { get; init; }
    public byte SampleG { get; init; }
    public byte SampleB { get; init; }
}
