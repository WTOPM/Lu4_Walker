using WTOPMBot.Models;

namespace WTOPMBot.Services;

public static class PixelProbeReader
{
    public static bool TryReadAll(
        nint hwnd,
        StatusBarProfile profile,
        out IReadOnlyList<PixelProbeReading> readings,
        out string? error)
    {
        readings = Array.Empty<PixelProbeReading>();
        error = null;

        if (profile.Probes.Count == 0)
            return true;

        if (!WindowCaptureService.TryGetClientSizePublic(hwnd, out var clientW, out var clientH))
        {
            error = "Не удалось получить размер окна.";
            return false;
        }

        var scaleX = clientW / (double)Math.Max(1, profile.ReferenceWidth);
        var scaleY = clientH / (double)Math.Max(1, profile.ReferenceHeight);
        var list = new List<PixelProbeReading>(profile.Probes.Count);

        foreach (var probe in profile.Probes)
        {
            var x = (int)Math.Round(probe.X * scaleX);
            var y = (int)Math.Round(probe.Y * scaleY);

            if (!WindowCaptureService.TryReadClientPixel(hwnd, x, y, out var r, out var g, out var b, out error))
                return false;

            var dist = ColorDistance(r, g, b, probe.R, probe.G, probe.B);
            list.Add(new PixelProbeReading
            {
                Id = probe.Id,
                Matched = dist <= probe.Tolerance,
                SampleR = r,
                SampleG = g,
                SampleB = b
            });
        }

        readings = list;
        error = null;
        return true;
    }

    public static CombatState BuildCombatState(
        IReadOnlyList<PixelProbeReading> probes,
        StatusBarReading? targetHp)
    {
        var selected = probes.FirstOrDefault(p => p.Id.Equals("target_selected", StringComparison.OrdinalIgnoreCase))?.Matched == true;
        var deathPixel = probes.FirstOrDefault(p => p.Id.Equals("target_dead", StringComparison.OrdinalIgnoreCase))?.Matched == true;
        // Any detected fill counts as "HP present" (bar shown in UI).
        var hasHp = targetHp is { Detected: true, FillPercent: > 0 };

        return new CombatState
        {
            TargetSelected = selected,
            DeathPixel = deathPixel,
            HasTargetHp = hasHp,
            TargetHp = targetHp
        };
    }

    private static int ColorDistance(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var dr = r1 - r2;
        var dg = g1 - g2;
        var db = b1 - b2;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
