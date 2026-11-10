using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

/// <summary>
/// Searches fixed client regions by bar colors, then locks the found rectangles
/// as percentages of the current client size (normalized to 2560×1440 when saved).
/// </summary>
public static class StatusBarAutoCalibrator
{
    public const int ReferenceWidth = 2560;
    public const int ReferenceHeight = 1440;

    /// <summary>Player CP/HP/MP panel search box at 2560×1440.</summary>
    public static readonly SearchRegion PlayerPanel = new(0, 0, 500, 100);

    /// <summary>Target HP search box at 2560×1440 (bar starts left of center nameplate).</summary>
    public static readonly SearchRegion TargetPanel = new(1050, 0, 1750, 140);

    public readonly record struct SearchRegion(int X1, int Y1, int X2, int Y2);

    public sealed class CalibrationResult
    {
        public bool PlayerOk { get; init; }
        public bool TargetOk { get; init; }
        public StatusBarDefinition? Cp { get; init; }
        public StatusBarDefinition? Hp { get; init; }
        public StatusBarDefinition? Mp { get; init; }
        public StatusBarDefinition? TargetHp { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    public static bool TryCalibrate(
        nint hwnd,
        out CalibrationResult result,
        out string? error)
    {
        result = new CalibrationResult { Summary = "не найдено" };
        error = null;

        if (!WindowCaptureService.TryGetClientSizePublic(hwnd, out var clientW, out var clientH))
        {
            error = "Не удалось получить размер окна.";
            return false;
        }

        var sx = clientW / (double)ReferenceWidth;
        var sy = clientH / (double)ReferenceHeight;

        var player = ScaleRegion(PlayerPanel, sx, sy, clientW, clientH);
        var target = ScaleRegion(TargetPanel, sx, sy, clientW, clientH);

        if (!WindowCaptureService.TryCaptureClientRegion(
                hwnd, player.X1, player.Y1, player.Width, player.Height,
                out var playerBmp, out error) || playerBmp is null)
            return false;

        StatusBarDefinition? cp;
        StatusBarDefinition? hp;
        StatusBarDefinition? mp;
        using (playerBmp)
        {
            cp = FindPlayerBar(playerBmp, player, clientW, clientH, "cp", "CP", IsCpColor, 215, 142, 23, 55);
            hp = FindPlayerBar(playerBmp, player, clientW, clientH, "hp", "HP", IsHpColor, 161, 57, 64, 55);
            mp = FindPlayerBar(playerBmp, player, clientW, clientH, "mp", "MP", IsMpColor, 65, 121, 206, 55);
        }

        if (!WindowCaptureService.TryCaptureClientRegion(
                hwnd, target.X1, target.Y1, target.Width, target.Height,
                out var targetBmp, out error) || targetBmp is null)
            return false;

        StatusBarDefinition? targetHp;
        using (targetBmp)
        {
            targetHp = FindTargetBar(targetBmp, target, clientW, clientH);
        }

        var playerOk = cp is not null && hp is not null && mp is not null;
        var targetOk = targetHp is not null;

        var parts = new List<string>(4);
        if (cp is not null) parts.Add("CP");
        if (hp is not null) parts.Add("HP");
        if (mp is not null) parts.Add("MP");
        if (targetHp is not null) parts.Add("TGT");

        result = new CalibrationResult
        {
            PlayerOk = playerOk,
            TargetOk = targetOk,
            Cp = cp,
            Hp = hp,
            Mp = mp,
            TargetHp = targetHp,
            Summary = parts.Count == 0
                ? "полоски не найдены"
                : "найдено: " + string.Join(", ", parts)
        };

        return playerOk || targetOk;
    }

    public static void ApplyToProfile(StatusBarProfile profile, CalibrationResult found)
    {
        profile.ReferenceWidth = ReferenceWidth;
        profile.ReferenceHeight = ReferenceHeight;
        profile.Probes = [];
        profile.Bars ??= [];

        Upsert(profile, found.Cp);
        Upsert(profile, found.Hp);
        Upsert(profile, found.Mp);
        Upsert(profile, found.TargetHp);
        StatusBarProfileStore.EnsureDeathSkullProbe(profile);
    }

    private static void Upsert(StatusBarProfile profile, StatusBarDefinition? bar)
    {
        if (bar is null)
            return;

        var idx = profile.Bars.FindIndex(b => b.Id.Equals(bar.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            profile.Bars[idx] = bar;
        else
            profile.Bars.Add(bar);
    }

    private readonly record struct ScaledRegion(int X1, int Y1, int Width, int Height);

    private static ScaledRegion ScaleRegion(SearchRegion region, double sx, double sy, int clientW, int clientH)
    {
        var x1 = Math.Clamp((int)Math.Floor(region.X1 * sx), 0, Math.Max(0, clientW - 1));
        var y1 = Math.Clamp((int)Math.Floor(region.Y1 * sy), 0, Math.Max(0, clientH - 1));
        var x2 = Math.Clamp((int)Math.Ceiling(region.X2 * sx), x1, clientW - 1);
        var y2 = Math.Clamp((int)Math.Ceiling(region.Y2 * sy), y1, clientH - 1);
        return new ScaledRegion(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
    }

    private static StatusBarDefinition? FindPlayerBar(
        Bitmap bitmap,
        ScaledRegion origin,
        int clientW,
        int clientH,
        string id,
        string name,
        Func<byte, byte, byte, bool> isFill,
        byte r,
        byte g,
        byte b,
        int tolerance)
    {
        var pixels = LockPixels(bitmap);
        try
        {
            // LU4 bars are slanted — lock to the filled parallelogram edge, never into world pixels.
            var minRun = Math.Max(35, bitmap.Width / 10);
            var rowRuns = new (int Start, int End)?[bitmap.Height];

            for (var y = 0; y < bitmap.Height; y++)
            {
                rowRuns[y] = FindFillSpan(pixels, bitmap.Width, y, isFill, minRun, gapTolerance: 4);
            }

            if (!FindBestBand(rowRuns, minHeight: 2, out var y1, out var y2, out _, out _))
                return null;

            // Widest clean row in the band (avoids text holes and slant inflation).
            if (!TryPickBestRow(rowRuns, y1, y2, out var rowY, out var x1, out var x2))
                return null;

            // Keep a tight vertical slice around the chosen row.
            var absX1 = origin.X1 + x1;
            var absY1 = origin.Y1 + Math.Max(y1, rowY - 1);
            var absX2 = origin.X1 + x2;
            var absY2 = origin.Y1 + Math.Min(y2, rowY + 1);

            return ToDefinition(id, name, absX1, absY1, absX2, absY2, clientW, clientH, r, g, b, tolerance);
        }
        finally
        {
            pixels.Dispose();
        }
    }

    private static StatusBarDefinition? FindTargetBar(
        Bitmap bitmap,
        ScaledRegion origin,
        int clientW,
        int clientH)
    {
        var pixels = LockPixels(bitmap);
        try
        {
            // Text like "314 / 314" punches holes — allow gaps and score by density.
            var minSpan = Math.Max(40, bitmap.Width / 10);
            var rowRuns = new (int Start, int End)?[bitmap.Height];

            for (var y = 0; y < bitmap.Height; y++)
            {
                rowRuns[y] = FindFillSpan(pixels, bitmap.Width, y, IsTargetFill, minSpan, gapTolerance: 18);
            }

            if (!FindBestBand(rowRuns, minHeight: 1, out var y1, out var y2, out _, out _))
                return null;

            if (!TryPickBestRow(rowRuns, y1, y2, out var rowY, out var x1, out var x2))
                return null;

            // Empty HP trough stays inside the dark nameplate — expand only a little, UI-grey only.
            ExpandTargetTrough(pixels, bitmap.Width, rowY, ref x1, ref x2);

            var absX1 = origin.X1 + x1;
            var absY1 = origin.Y1 + Math.Max(0, rowY - 1);
            var absX2 = origin.X1 + x2;
            var absY2 = origin.Y1 + Math.Min(bitmap.Height - 1, rowY + 2);

            return ToDefinition("target_hp", "Target HP", absX1, absY1, absX2, absY2, clientW, clientH, 161, 57, 64, 48);
        }
        finally
        {
            pixels.Dispose();
        }
    }

    private static StatusBarDefinition ToDefinition(
        string id,
        string name,
        int x1,
        int y1,
        int x2,
        int y2,
        int clientW,
        int clientH,
        byte r,
        byte g,
        byte b,
        int tolerance)
    {
        var x1Pct = x1 * 100.0 / clientW;
        var y1Pct = y1 * 100.0 / clientH;
        var x2Pct = x2 * 100.0 / clientW;
        var y2Pct = y2 * 100.0 / clientH;

        return new StatusBarDefinition
        {
            Id = id,
            Name = name,
            X1Pct = Math.Round(x1Pct, 4),
            Y1Pct = Math.Round(y1Pct, 4),
            X2Pct = Math.Round(x2Pct, 4),
            Y2Pct = Math.Round(y2Pct, 4),
            // Absolute coords stored for 2560×1440 readability.
            X1 = (int)Math.Round(x1Pct / 100.0 * ReferenceWidth),
            Y1 = (int)Math.Round(y1Pct / 100.0 * ReferenceHeight),
            X2 = (int)Math.Round(x2Pct / 100.0 * ReferenceWidth),
            Y2 = (int)Math.Round(y2Pct / 100.0 * ReferenceHeight),
            R = r,
            G = g,
            B = b,
            Tolerance = tolerance
        };
    }

    private static bool TryPickBestRow(
        (int Start, int End)?[] rowRuns,
        int y1,
        int y2,
        out int bestY,
        out int x1,
        out int x2)
    {
        bestY = y1;
        x1 = x2 = 0;
        var bestWidth = -1;

        for (var y = y1; y <= y2; y++)
        {
            if (rowRuns[y] is not { } run)
                continue;

            var w = run.End - run.Start + 1;
            if (w > bestWidth)
            {
                bestWidth = w;
                bestY = y;
                x1 = run.Start;
                x2 = run.End;
            }
        }

        return bestWidth > 0;
    }

    /// <summary>
    /// Finds the widest fill span on a row, allowing small gaps (AA / overlay text).
    /// </summary>
    private static (int Start, int End)? FindFillSpan(
        PixelBuffer pixels,
        int width,
        int y,
        Func<byte, byte, byte, bool> isFill,
        int minSpan,
        int gapTolerance)
    {
        var bestStart = -1;
        var bestEnd = -1;
        var bestHits = 0;

        var spanStart = -1;
        var lastHit = -1;
        var hits = 0;

        for (var x = 0; x < width; x++)
        {
            pixels.Get(x, y, out var r, out var g, out var b);
            if (isFill(r, g, b))
            {
                if (spanStart < 0)
                    spanStart = x;
                lastHit = x;
                hits++;
            }
            else if (spanStart >= 0)
            {
                if (x - lastHit <= gapTolerance)
                    continue;

                if (hits > bestHits && lastHit - spanStart + 1 >= minSpan)
                {
                    bestStart = spanStart;
                    bestEnd = lastHit;
                    bestHits = hits;
                }

                spanStart = -1;
                lastHit = -1;
                hits = 0;
            }
        }

        if (spanStart >= 0 && hits > bestHits && lastHit - spanStart + 1 >= minSpan)
        {
            bestStart = spanStart;
            bestEnd = lastHit;
            bestHits = hits;
        }

        if (bestStart < 0)
            return null;

        // Require enough real fill so sparse foliage noise is rejected.
        var spanWidth = bestEnd - bestStart + 1;
        if (bestHits < Math.Max(minSpan / 2, 20) || bestHits < spanWidth * 0.35)
            return null;

        return (bestStart, bestEnd);
    }

    private static bool FindBestBand(
        (int Start, int End)?[] rowRuns,
        int minHeight,
        out int y1,
        out int y2,
        out int x1,
        out int x2)
    {
        y1 = y2 = x1 = x2 = 0;
        var bestScore = 0;
        var bestY1 = -1;
        var bestY2 = -1;
        var bestX1 = 0;
        var bestX2 = 0;

        var y = 0;
        while (y < rowRuns.Length)
        {
            if (rowRuns[y] is null)
            {
                y++;
                continue;
            }

            var startY = y;
            var minX = rowRuns[y]!.Value.Start;
            var maxX = rowRuns[y]!.Value.End;
            var score = maxX - minX + 1;
            y++;

            while (y < rowRuns.Length && rowRuns[y] is { } run)
            {
                // Require horizontal overlap with the growing band.
                if (run.End < minX - 8 || run.Start > maxX + 8)
                    break;

                minX = Math.Min(minX, run.Start);
                maxX = Math.Max(maxX, run.End);
                score += run.End - run.Start + 1;
                y++;
            }

            var endY = y - 1;
            var height = endY - startY + 1;
            if (height >= minHeight && score > bestScore)
            {
                bestScore = score;
                bestY1 = startY;
                bestY2 = endY;
                bestX1 = minX;
                bestX2 = maxX;
            }
        }

        if (bestY1 < 0)
            return false;

        y1 = bestY1;
        y2 = bestY2;
        x1 = bestX1;
        x2 = bestX2;
        return true;
    }

    /// <summary>
    /// Extends only through dark UI plate pixels (not world terrain).
    /// </summary>
    private static void ExpandTargetTrough(PixelBuffer pixels, int width, int y, ref int x1, ref int x2)
    {
        var maxExpand = Math.Max(24, width / 8);
        var expanded = 0;
        while (x1 > 0 && expanded < maxExpand)
        {
            pixels.Get(x1 - 1, y, out var r, out var g, out var b);
            if (IsTargetFill(r, g, b) || IsNameplateChrome(r, g, b))
            {
                x1--;
                expanded++;
            }
            else break;
        }

        expanded = 0;
        while (x2 < width - 1 && expanded < maxExpand)
        {
            pixels.Get(x2 + 1, y, out var r, out var g, out var b);
            if (IsTargetFill(r, g, b) || IsNameplateChrome(r, g, b))
            {
                x2++;
                expanded++;
            }
            else break;
        }
    }

    private static bool IsCpColor(byte r, byte g, byte b) =>
        r > 140 && g > 80 && b < 110 && r >= g && r - b > 40;

    private static bool IsHpColor(byte r, byte g, byte b) =>
        r > 100 && r > g + 30 && r > b + 30 && g < 110 && b < 110;

    private static bool IsMpColor(byte r, byte g, byte b) =>
        b > 100 && b > r + 20 && b >= g - 15 && r < 120;

    private static bool IsTargetFill(byte r, byte g, byte b)
    {
        // Deep red / maroon of LU4 target bar (text AA may darken samples).
        if (r < 110)
            return false;
        if (g > 105 || b > 105)
            return false;
        if (r - g < 28 || r - b < 25)
            return false;
        // Reject yellow/green foliage (G stays close to R).
        if (g > r * 0.72)
            return false;
        return true;
    }

    private static bool IsNameplateChrome(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max > 70)
            return false;
        if (g > r + 12 && g > b + 8)
            return false;
        return max - min <= 28;
    }

    private sealed class PixelBuffer : IDisposable
    {
        private readonly Bitmap _bitmap;
        private readonly BitmapData _data;
        private readonly byte[] _bytes;
        private readonly int _stride;
        private readonly int _width;
        private readonly int _height;

        public PixelBuffer(Bitmap bitmap)
        {
            _bitmap = bitmap;
            _width = bitmap.Width;
            _height = bitmap.Height;
            var rect = new Rectangle(0, 0, _width, _height);
            _data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            _stride = Math.Abs(_data.Stride);
            _bytes = new byte[_stride * _height];
            Marshal.Copy(_data.Scan0, _bytes, 0, _bytes.Length);
        }

        public void Get(int x, int y, out byte r, out byte g, out byte b)
        {
            var i = y * _stride + x * 4;
            b = _bytes[i];
            g = _bytes[i + 1];
            r = _bytes[i + 2];
        }

        public void Dispose() => _bitmap.UnlockBits(_data);
    }

    private static PixelBuffer LockPixels(Bitmap bitmap) => new(bitmap);
}
