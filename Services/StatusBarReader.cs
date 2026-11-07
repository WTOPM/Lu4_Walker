using System.Drawing;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

public static class StatusBarReader
{
    public static int LastClientWidth { get; private set; }
    public static int LastClientHeight { get; private set; }
    public static double LastScaleX { get; private set; } = 1;
    public static double LastScaleY { get; private set; } = 1;

    public static bool TryReadAll(
        nint hwnd,
        StatusBarProfile profile,
        out IReadOnlyList<StatusBarReading> readings,
        out string? error)
    {
        readings = Array.Empty<StatusBarReading>();
        error = null;

        if (profile.Bars.Count == 0)
        {
            error = "Профиль полосок пуст.";
            return false;
        }

        if (!WindowCaptureService.TryGetClientSizePublic(hwnd, out var clientW, out var clientH))
        {
            error = "Не удалось получить размер окна.";
            return false;
        }

        LastClientWidth = clientW;
        LastClientHeight = clientH;

        var refW = Math.Max(1, profile.ReferenceWidth);
        var refH = Math.Max(1, profile.ReferenceHeight);
        var scaleX = clientW / (double)refW;
        var scaleY = clientH / (double)refH;
        LastScaleX = scaleX;
        LastScaleY = scaleY;

        var list = new List<StatusBarReading>(profile.Bars.Count);

        foreach (var raw in profile.Bars)
        {
            var bar = ScaleBar(raw, scaleX, scaleY);

            var y1 = Math.Min(bar.Y1, bar.Y2);
            var y2 = Math.Max(bar.Y1, bar.Y2);
            if (y2 < y1)
                y2 = y1;

            // Target HP: always sample a few rows (bottom edge alone is fragile).
            if (IsTargetHp(bar) && y2 - y1 < 4)
            {
                y1 = Math.Max(0, y2 - 4);
            }

            var x1 = Math.Clamp(Math.Min(bar.X1, bar.X2), 0, clientW - 1);
            var x2 = Math.Clamp(Math.Max(bar.X1, bar.X2), 0, clientW - 1);
            y1 = Math.Clamp(y1, 0, clientH - 1);
            y2 = Math.Clamp(y2, 0, clientH - 1);

            if (x2 <= x1)
            {
                list.Add(EmptyReading(bar));
                continue;
            }

            bar.X1 = x1;
            bar.X2 = x2;
            bar.Y1 = y1;
            bar.Y2 = y2;

            if (!WindowCaptureService.TryCaptureClientRegion(
                    hwnd,
                    x1,
                    y1,
                    x2 - x1 + 1,
                    y2 - y1 + 1,
                    out var bitmap,
                    out error) || bitmap is null)
            {
                return false;
            }

            using (bitmap)
            {
                list.Add(IsTargetHp(bar)
                    ? MeasureTargetHp(bitmap, bar, originX: x1, originY: y1)
                    : MeasureBar(bitmap, bar, originX: x1, originY: y1));
            }
        }

        readings = list;
        error = null;
        return true;
    }

    private static bool IsTargetHp(StatusBarDefinition bar) =>
        bar.Id.Equals("target_hp", StringComparison.OrdinalIgnoreCase);

    private static StatusBarReading EmptyReading(StatusBarDefinition bar) => new()
    {
        Id = bar.Id,
        Name = bar.Name,
        FillPercent = 0,
        Detected = false
    };

    private static StatusBarDefinition ScaleBar(StatusBarDefinition bar, double scaleX, double scaleY) => new()
    {
        Id = bar.Id,
        Name = bar.Name,
        X1 = (int)Math.Round(bar.X1 * scaleX),
        Y1 = (int)Math.Round(bar.Y1 * scaleY),
        X2 = (int)Math.Round(bar.X2 * scaleX),
        Y2 = (int)Math.Round(bar.Y2 * scaleY),
        R = bar.R,
        G = bar.G,
        B = bar.B,
        Tolerance = bar.Tolerance
    };

    /// <summary>
    /// Target HP over world background: need contiguous burgundy, not speckled leaves.
    /// </summary>
    private static StatusBarReading MeasureTargetHp(Bitmap bitmap, StatusBarDefinition bar, int originX, int originY)
    {
        var localX1 = Math.Clamp(bar.X1 - originX, 0, bitmap.Width - 1);
        var localY1 = Math.Clamp(bar.Y1 - originY, 0, bitmap.Height - 1);
        var localX2 = Math.Clamp(bar.X2 - originX, 0, bitmap.Width - 1);
        var localY2 = Math.Clamp(bar.Y2 - originY, 0, bitmap.Height - 1);

        if (localX2 < localX1 || localY2 < localY1)
            return EmptyReading(bar);

        var width = localX2 - localX1 + 1;
        StatusBarReading? best = null;

        // Pick the best row in the strip.
        for (var y = localY1; y <= localY2; y++)
        {
            var reading = MeasureTargetHpRow(bitmap, bar, localX1, localX2, y, width);
            if (best is null)
            {
                best = reading;
                continue;
            }

            if (reading.Detected && (!best.Detected || reading.FillPercent > best.FillPercent))
                best = reading;
        }

        return best ?? EmptyReading(bar);
    }

    private static StatusBarReading MeasureTargetHpRow(
        Bitmap bitmap,
        StatusBarDefinition bar,
        int localX1,
        int localX2,
        int y,
        int width)
    {
        var first = bitmap.GetPixel(localX1, y);

        var fillFlags = new bool[width];
        var emptyHits = 0;
        var fillHits = 0;

        for (var i = 0; i < width; i++)
        {
            var c = bitmap.GetPixel(localX1 + i, y);
            if (IsTargetHpFill(c.R, c.G, c.B, bar))
            {
                fillFlags[i] = true;
                fillHits++;
            }
            else if (IsTargetHpEmptyTrough(c.R, c.G, c.B))
            {
                emptyHits++;
            }
        }

        // Longest contiguous fill run (leaves = short speckles, bar = long run).
        var longest = 0;
        var run = 0;
        for (var i = 0; i < width; i++)
        {
            if (fillFlags[i])
            {
                run++;
                if (run > longest)
                    longest = run;
            }
            else
            {
                run = 0;
            }
        }

        // Fill % from left with small gaps (digits / AA), requiring a real contiguous body.
        var lastFill = -1;
        var miss = 0;
        const int missLimit = 14;
        var matched = 0;
        for (var i = 0; i < width; i++)
        {
            if (fillFlags[i])
            {
                lastFill = i;
                miss = 0;
                matched++;
            }
            else if (lastFill >= 0)
            {
                miss++;
                if (miss >= missLimit)
                    break;
            }
        }

        var minRun = Math.Max(40, width / 10);
        if (lastFill >= 0 && longest >= minRun)
        {
            var runWidth = lastFill + 1;
            var density = matched / (double)runWidth;
            if (density >= 0.50 && matched >= 25)
            {
                return new StatusBarReading
                {
                    Id = bar.Id,
                    Name = bar.Name,
                    FillPercent = Math.Clamp((lastFill + 1) / (double)width, 0, 1) * 100.0,
                    Detected = true,
                    SampleR = first.R,
                    SampleG = first.G,
                    SampleB = first.B
                };
            }
        }

        // Also accept "mostly full" when almost the whole line is fill (full HP, slight AA).
        var fillRatio = fillHits / (double)width;
        if (fillRatio >= 0.55 && longest >= minRun)
        {
            return new StatusBarReading
            {
                Id = bar.Id,
                Name = bar.Name,
                FillPercent = Math.Clamp(fillRatio, 0, 1) * 100.0,
                Detected = true,
                SampleR = first.R,
                SampleG = first.G,
                SampleB = first.B
            };
        }

        // Empty target frame.
        var emptyRatio = emptyHits / (double)width;
        if (emptyRatio >= 0.35 && fillRatio < 0.12 && longest < minRun)
        {
            return new StatusBarReading
            {
                Id = bar.Id,
                Name = bar.Name,
                FillPercent = 0,
                Detected = true,
                SampleR = first.R,
                SampleG = first.G,
                SampleB = first.B
            };
        }

        return new StatusBarReading
        {
            Id = bar.Id,
            Name = bar.Name,
            FillPercent = 0,
            Detected = false,
            SampleR = first.R,
            SampleG = first.G,
            SampleB = first.B
        };
    }

    private static StatusBarReading MeasureBar(Bitmap bitmap, StatusBarDefinition bar, int originX, int originY)
    {
        var localX1 = Math.Clamp(bar.X1 - originX, 0, bitmap.Width - 1);
        var localY1 = Math.Clamp(bar.Y1 - originY, 0, bitmap.Height - 1);
        var localX2 = Math.Clamp(bar.X2 - originX, 0, bitmap.Width - 1);
        var localY2 = Math.Clamp(bar.Y2 - originY, 0, bitmap.Height - 1);

        if (localX2 < localX1 || localY2 < localY1)
            return EmptyReading(bar);

        var height = localY2 - localY1 + 1;
        int[] scanRows;
        if (height <= 2)
        {
            scanRows = Enumerable.Range(localY1, height).ToArray();
        }
        else
        {
            scanRows =
            [
                localY1 + Math.Max(0, height / 5),
                localY1 + Math.Max(0, height / 4),
                localY2 - Math.Max(0, height / 5)
            ];
            scanRows = scanRows.Where(y => y >= localY1 && y <= localY2).Distinct().ToArray();
        }

        var bestFill = 0.0;
        var detected = false;
        byte sampleR = 0, sampleG = 0, sampleB = 0;
        var sampled = false;

        foreach (var y in scanRows)
        {
            var fill = MeasureRow(bitmap, bar, localX1, localX2, y, out var rowDetected, out var r, out var g, out var b);
            if (!sampled)
            {
                sampleR = r;
                sampleG = g;
                sampleB = b;
                sampled = true;
            }

            if (rowDetected)
                detected = true;

            if (fill > bestFill)
                bestFill = fill;
        }

        return new StatusBarReading
        {
            Id = bar.Id,
            Name = bar.Name,
            FillPercent = bestFill * 100.0,
            Detected = detected,
            SampleR = sampleR,
            SampleG = sampleG,
            SampleB = sampleB
        };
    }

    private static double MeasureRow(
        Bitmap bitmap,
        StatusBarDefinition bar,
        int localX1,
        int localX2,
        int y,
        out bool detected,
        out byte sampleR,
        out byte sampleG,
        out byte sampleB)
    {
        var width = localX2 - localX1 + 1;
        var lastFill = -1;
        var missStreak = 0;
        const int missLimit = 40;

        var first = bitmap.GetPixel(localX1, y);
        sampleR = first.R;
        sampleG = first.G;
        sampleB = first.B;

        for (var x = localX1; x <= localX2; x++)
        {
            var c = bitmap.GetPixel(x, y);
            if (IsFillColor(c.R, c.G, c.B, bar))
            {
                lastFill = x - localX1;
                missStreak = 0;
            }
            else if (lastFill >= 0)
            {
                missStreak++;
                if (missStreak >= missLimit)
                    break;
            }
        }

        detected = lastFill >= 0;
        if (!detected)
            return 0;

        return Math.Clamp((lastFill + 1) / (double)width, 0, 1);
    }

    /// <summary>
    /// Burgundy target HP. Rejects yellow foliage (G close to R) but allows AA darker reds.
    /// </summary>
    private static bool IsTargetHpFill(byte r, byte g, byte b, StatusBarDefinition bar)
    {
        if (r < 105)
            return false;

        // Yellow / lime leaves: green stays high relative to red.
        if (g > r * 0.62)
            return false;

        // Strong green or yellow cast.
        if (g > 110)
            return false;

        if (r - g < 35 || r - b < 30)
            return false;

        if (b > 110)
            return false;

        var tol = Math.Clamp(bar.Tolerance, 28, 48);
        return ColorDistance(r, g, b, bar.R, bar.G, bar.B) <= tol
               || IsNearBurgundy(r, g, b);
    }

    private static bool IsNearBurgundy(byte r, byte g, byte b) =>
        r is >= 120 and <= 200
        && g is >= 35 and <= 95
        && b is >= 35 and <= 100
        && r - g >= 45
        && r - b >= 40;

    private static bool IsTargetHpEmptyTrough(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max > 78)
            return false;
        if (g > r + 14 && g > b + 8)
            return false;
        return max - min <= 32;
    }

    private static bool IsFillColor(byte r, byte g, byte b, StatusBarDefinition bar)
    {
        if (ColorDistance(r, g, b, bar.R, bar.G, bar.B) > bar.Tolerance)
            return false;

        return bar.Id.ToLowerInvariant() switch
        {
            "cp" => r > 140 && g > 80 && b < 100 && r >= g,
            "hp" => r > 100 && r > g + 30 && r > b + 30,
            "mp" => b > 100 && b > r + 20 && b >= g - 10,
            _ => true
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
