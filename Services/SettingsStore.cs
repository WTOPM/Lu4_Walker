using System.IO;
using System.Text.Json;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.KeyBinds ??= KeyBindProfile.CreateDefault();
            settings.KeyBinds.EnsureComplete();
            settings.NextTargetAttempts = Math.Clamp(settings.NextTargetAttempts, 0, 20);
            settings.ApproachNextTargetIntervalMs = Math.Clamp(settings.ApproachNextTargetIntervalMs, 300, 10000);
            settings.LootAttempts = Math.Clamp(settings.LootAttempts, 1, 20);
            settings.LootIntervalMs = Math.Clamp(settings.LootIntervalMs, 200, 10000);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
