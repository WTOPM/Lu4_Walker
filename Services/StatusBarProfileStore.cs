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
                if (loaded is not null)
                {
                    loaded.Probes ??= [];
                    loaded.Bars ??= [];
                    EnsureDeathSkullProbe(loaded);
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall through to embedded defaults.
        }

        var defaults = CreateDefault();
        Save(defaults);
        return defaults;
    }

    public static void Save(StatusBarProfile profile)
    {
        try
        {
            EnsureDeathSkullProbe(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
            File.WriteAllText(DefaultPath, JsonSerializer.Serialize(profile, JsonOptions));
        }
        catch
        {
            // Ignore write failures (e.g. read-only folder).
        }
    }

    /// <summary>
    /// Empty bars — coordinates come from <see cref="StatusBarAutoCalibrator"/>.
    /// Skull probe for corpse detection is always ensured.
    /// </summary>
    public static StatusBarProfile CreateDefault()
    {
        var profile = new StatusBarProfile
        {
            ReferenceWidth = StatusBarAutoCalibrator.ReferenceWidth,
            ReferenceHeight = StatusBarAutoCalibrator.ReferenceHeight,
            AutoCalibrated = false,
            Bars = [],
            Probes = []
        };
        EnsureDeathSkullProbe(profile);
        return profile;
    }

    /// <summary>
    /// Portrait skull region from 2560×1440 captures (alive wasp vs dead skull).
    /// Region [1325,48]–[1341,64], RGB(45,20,20)±15 → alive 0 / dead ~87 matches.
    /// </summary>
    public static void EnsureDeathSkullProbe(StatusBarProfile profile)
    {
        profile.Probes ??= [];
        profile.Probes.RemoveAll(p =>
            p.Id.Equals(PixelProbeReader.TargetDeadId, StringComparison.OrdinalIgnoreCase));

        profile.Probes.Add(new PixelProbe
        {
            Id = PixelProbeReader.TargetDeadId,
            Name = "Череп (смерть)",
            X = 1325,
            Y = 48,
            Width = 17,
            Height = 17,
            R = 45,
            G = 20,
            B = 20,
            Tolerance = 15,
            MinMatches = 30
        });
    }
}
