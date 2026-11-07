using System.IO;
using System.Text.Json;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

public static class StatusBarProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "profiles", "player_bars.json");

    public static StatusBarProfile LoadOrCreateDefault()
    {
        try
        {
            if (File.Exists(DefaultPath))
            {
                var json = File.ReadAllText(DefaultPath);
                var loaded = JsonSerializer.Deserialize<StatusBarProfile>(json, JsonOptions);
                if (loaded?.Bars is { Count: > 0 })
                    return loaded;
            }
        }
        catch
        {
            // Fall through to embedded defaults.
        }

        var defaults = CreateDefault();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
            File.WriteAllText(DefaultPath, JsonSerializer.Serialize(defaults, JsonOptions));
        }
        catch
        {
            // Ignore write failures (e.g. read-only folder).
        }

        return defaults;
    }

    public static StatusBarProfile CreateDefault() => new()
    {
        ReferenceWidth = 2560,
        ReferenceHeight = 1440,
        Bars =
        [
            new StatusBarDefinition
            {
                Id = "cp", Name = "CP",
                X1 = 70, Y1 = 12, X2 = 409, Y2 = 30,
                R = 215, G = 142, B = 23, Tolerance = 55
            },
            new StatusBarDefinition
            {
                Id = "hp", Name = "HP",
                X1 = 70, Y1 = 31, X2 = 421, Y2 = 49,
                R = 161, G = 57, B = 64, Tolerance = 55
            },
            new StatusBarDefinition
            {
                Id = "mp", Name = "MP",
                X1 = 70, Y1 = 50, X2 = 432, Y2 = 68,
                R = 65, G = 121, B = 206, Tolerance = 55
            },
            new StatusBarDefinition
            {
                Id = "target_hp", Name = "Target HP",
                X1 = 1365, Y1 = 122, X2 = 1749, Y2 = 126,
                R = 161, G = 57, B = 64, Tolerance = 42
            }
        ],
        Probes =
        [
            new PixelProbe
            {
                Id = "target_selected", Name = "Target selected",
                X = 1665, Y = 28, R = 224, G = 211, B = 143, Tolerance = 35
            },
            new PixelProbe
            {
                Id = "target_dead", Name = "Target dead",
                X = 1345, Y = 52, R = 132, G = 85, B = 90, Tolerance = 35
            }
        ]
    };
}
