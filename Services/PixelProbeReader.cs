using System.Drawing;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

public static class PixelProbeReader
{
    public const string TargetDeadId = "target_dead";

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
            var reading = ReadProbe(hwnd, probe, clientW, clientH, scaleX, scaleY, out error);
            if (reading is null)
                return false;
            list.Add(reading);
        }

        readings = list;
        error = null;
        return true;
    }

    public static CombatState BuildCombatState(
        IReadOnlyList<PixelProbeReading> probes,
        StatusBarReading? targetHp)
    {
        var death = probes.Any(p =>
            p.Id.Equals(TargetDeadId, StringComparison.OrdinalIgnoreCase) && p.Matched);
        return CombatState.FromTargetHp(targetHp, death);
    }

    private static PixelProbeReading? ReadProbe(
        nint hwnd,
        PixelProbe probe,
        int clientW,
        int clientH,
        double scaleX,
        double scaleY,
        out string? error)
    {
        error = null;
        var x = (int)Math.Round(probe.X * scaleX);
        var y = (int)Math.Round(probe.Y * scaleY);
        var w = Math.Max(1, (int)Math.Round(Math.Max(1, probe.Width) * scaleX));
        var h = Math.Max(1, (int)Math.Round(Math.Max(1, probe.Height) * scaleY));

        x = Math.Clamp(x, 0, clientW - 1);
        y = Math.Clamp(y, 0, clientH - 1);
        w = Math.Clamp(w, 1, clientW - x);
        h = Math.Clamp(h, 1, clientH - y);

        if (w <= 1 && h <= 1)
        {
            if (!WindowCaptureService.TryReadClientPixel(hwnd, x, y, out var r, out var g, out var b, out error))
                return null;

            var matched = ColorDistance(r, g, b, probe.R, probe.G, probe.B) <= probe.Tolerance;
            return new PixelProbeReading
            {
                Id = probe.Id,
                Matched = matched && probe.MinMatches <= 1,
                MatchCount = matched ? 1 : 0,
                SampleR = r,
                SampleG = g,
                SampleB = b
            };
        }

        if (!WindowCaptureService.TryCaptureClientRegion(hwnd, x, y, w, h, out var bitmap, out error) ||
            bitmap is null)
            return null;

        using (bitmap)
        {
            var matches = 0;
            byte sampleR = 0, sampleG = 0, sampleB = 0;
            var tol = Math.Max(0, probe.Tolerance);

            for (var py = 0; py < bitmap.Height; py++)
            {
                for (var px = 0; px < bitmap.Width; px++)
                {
                    var c = bitmap.GetPixel(px, py);
                    if (ColorDistance(c.R, c.G, c.B, probe.R, probe.G, probe.B) <= tol)
                    {
                        matches++;
                        if (matches == 1)
                        {
                            sampleR = c.R;
                            sampleG = c.G;
                            sampleB = c.B;
                        }
                    }
                }
            }

            var min = Math.Max(1, probe.MinMatches);
            return new PixelProbeReading
            {
                Id = probe.Id,
                Matched = matches >= min,
                MatchCount = matches,
                SampleR = sampleR,
                SampleG = sampleG,
                SampleB = sampleB
            };
        }
    }

    private static int ColorDistance(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var dr = r1 - r2;
        var dg = g1 - g2;
        var db = b1 - b2;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
