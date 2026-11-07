using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

/// <summary>
/// Captures only the target HWND content (not the desktop composite),
/// so taskbar / overlay windows are not included.
/// </summary>
public static class WindowCaptureService
{
    private const uint PwClientOnly = 0x00000001;
    private const uint PwRenderFullContent = 0x00000002;

    public static string CaptureToBmp(ProcessEntry process, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(process);

        var hwnd = process.MainWindowHandle;
        if (hwnd == nint.Zero)
            throw new InvalidOperationException("У процесса нет главного окна.");

        if (!IsWindow(hwnd))
            throw new InvalidOperationException("Окно процесса уже закрыто. Обновите список.");

        if (IsIconic(hwnd))
            throw new InvalidOperationException("Окно свёрнуто. Разверните игру и повторите.");

        if (!TryGetClientSize(hwnd, out var width, out var height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("Не удалось получить размеры клиентской области окна.");

        Directory.CreateDirectory(outputDirectory);

        var fileName = $"{Sanitize(process.Name)}_{process.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.bmp";
        var fullPath = Path.Combine(outputDirectory, fileName);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var captured = TryPrintWindow(hwnd, bitmap, PwClientOnly | PwRenderFullContent)
                       || TryPrintWindow(hwnd, bitmap, PwRenderFullContent)
                       || TryPrintWindow(hwnd, bitmap, PwClientOnly)
                       || TryPrintWindow(hwnd, bitmap, 0)
                       || TryBitBltWindow(hwnd, bitmap);

        if (!captured || IsMostlyBlack(bitmap))
            throw new InvalidOperationException(
                "Не удалось снять содержимое окна игры (чёрный кадр). " +
                "Оставьте игру в оконном/безрамочном режиме и поверх других окон.");

        bitmap.Save(fullPath, ImageFormat.Bmp);
        return fullPath;
    }

    public static bool TryGetClientSizePublic(nint hwnd, out int width, out int height) =>
        TryGetClientSize(hwnd, out width, out height);

    /// <summary>
    /// Captures a rectangle from the game window client area (screen composite at that rect).
    /// Caller must Dispose the returned bitmap.
    /// </summary>
    public static bool TryCaptureClientRegion(
        nint hwnd,
        int clientX,
        int clientY,
        int width,
        int height,
        out Bitmap? bitmap,
        out string? error)
    {
        bitmap = null;
        error = null;

        if (hwnd == nint.Zero || !IsWindow(hwnd))
        {
            error = "Окно процесса недоступно.";
            return false;
        }

        if (IsIconic(hwnd))
        {
            error = "Окно свёрнуто.";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = "Некорректный размер области.";
            return false;
        }

        if (!TryGetClientSize(hwnd, out var clientW, out var clientH))
        {
            error = "Не удалось получить размер окна.";
            return false;
        }

        if (clientX < 0 || clientY < 0 || clientX + width > clientW || clientY + height > clientH)
        {
            error = "Область полосок выходит за границы окна.";
            return false;
        }

        var point = new POINT { X = clientX, Y = clientY };
        if (!ClientToScreen(hwnd, ref point))
        {
            error = "Не удалось преобразовать координаты.";
            return false;
        }

        try
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bmp))
            {
                graphics.CopyFromScreen(point.X, point.Y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            }

            bitmap = bmp;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reads one pixel from the game window client area (same coordinate space as BMP captures).
    /// Uses a 1×1 screen sample at the client point — fast enough for realtime loops.
    /// </summary>
    public static bool TryReadClientPixel(nint hwnd, int x, int y, out byte r, out byte g, out byte b, out string? error)
    {
        r = g = b = 0;
        error = null;

        if (hwnd == nint.Zero || !IsWindow(hwnd))
        {
            error = "Окно процесса недоступно.";
            return false;
        }

        if (IsIconic(hwnd))
        {
            error = "Окно свёрнуто.";
            return false;
        }

        if (!TryGetClientSize(hwnd, out var width, out var height))
        {
            error = "Не удалось получить размер окна.";
            return false;
        }

        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            error = $"Координаты вне окна (0..{width - 1}, 0..{height - 1}).";
            return false;
        }

        var point = new POINT { X = x, Y = y };
        if (!ClientToScreen(hwnd, ref point))
        {
            error = "Не удалось преобразовать координаты.";
            return false;
        }

        try
        {
            using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(point.X, point.Y, 0, 0, new System.Drawing.Size(1, 1), CopyPixelOperation.SourceCopy);
            }

            var color = bitmap.GetPixel(0, 0);
            r = color.R;
            g = color.G;
            b = color.B;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryPrintWindow(nint hwnd, Bitmap bitmap, uint flags)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        var hdc = graphics.GetHdc();
        try
        {
            if (!PrintWindow(hwnd, hdc, flags))
                return false;
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return !IsMostlyBlack(bitmap);
    }

    private static bool TryBitBltWindow(nint hwnd, Bitmap bitmap)
    {
        var windowDc = GetWindowDC(hwnd);
        if (windowDc == nint.Zero)
            return false;

        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            var destDc = graphics.GetHdc();
            try
            {
                // Copy client area from the window DC (not the desktop).
                if (!GetClientRect(hwnd, out var client))
                    return false;

                var width = client.Right - client.Left;
                var height = client.Bottom - client.Top;
                if (width <= 0 || height <= 0)
                    return false;

                // Window DC origin includes non-client; map client origin.
                var point = new POINT { X = 0, Y = 0 };
                ClientToScreen(hwnd, ref point);
                if (!GetWindowRect(hwnd, out var windowRect))
                    return false;

                var offsetX = point.X - windowRect.Left;
                var offsetY = point.Y - windowRect.Top;

                return BitBlt(destDc, 0, 0, width, height, windowDc, offsetX, offsetY, TernaryRasterOperations.SrcCopy);
            }
            finally
            {
                graphics.ReleaseHdc(destDc);
            }
        }
        finally
        {
            ReleaseDC(hwnd, windowDc);
        }
    }

    private static bool TryGetClientSize(nint hwnd, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!GetClientRect(hwnd, out var client))
            return false;

        width = client.Right - client.Left;
        height = client.Bottom - client.Top;
        return width > 0 && height > 0;
    }

    private static bool IsMostlyBlack(Bitmap bitmap)
    {
        const int stepX = 32;
        const int stepY = 32;
        var samples = 0;
        var dark = 0;

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                samples++;
                if (c.R < 8 && c.G < 8 && c.B < 8)
                    dark++;
            }
        }

        return samples > 0 && dark * 100 / samples >= 96;
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "process" : value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private enum TernaryRasterOperations : uint
    {
        SrcCopy = 0x00CC0020
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, TernaryRasterOperations rop);
}
