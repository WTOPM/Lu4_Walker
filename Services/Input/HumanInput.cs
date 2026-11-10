using WTOPMBot.Models;

namespace WTOPMBot.Services.Input;

/// <summary>
/// Human-like timing and letter-only decoys.
/// Decoys never use game General shortcuts or symbol keys.
/// </summary>
public static class HumanInput
{
    public static IReadOnlySet<byte> ReservedVirtualKeys { get; } = BuildReserved();
    public static IReadOnlyList<byte> DecoyVirtualKeys { get; } = BuildDecoyPool();

    public static int NextActionIntervalMs()
    {
        var roll = Random.Shared.Next(100);
        if (roll < 12)
            return Random.Shared.Next(180, 350);
        if (roll < 28)
            return Random.Shared.Next(900, 1600);
        return Random.Shared.Next(320, 1100);
    }

    public static int NextKeyHoldMs()
    {
        var roll = Random.Shared.Next(100);
        if (roll < 10)
            return Random.Shared.Next(32, 58);
        if (roll < 25)
            return Random.Shared.Next(150, 240);
        return Random.Shared.Next(55, 145);
    }

    public static int NextPrePressDelayMs() =>
        Random.Shared.Next(15, 85);

    public static int NextDecoyGapMs() =>
        Random.Shared.Next(90, 420);

    public static int NextDecoyCount()
    {
        if (DecoyVirtualKeys.Count == 0)
            return 0;

        if (Random.Shared.Next(100) >= Random.Shared.Next(20, 41))
            return 0;

        return Random.Shared.Next(100) < 30 ? 2 : 1;
    }

    public static byte PickDecoyVirtualKey()
    {
        if (DecoyVirtualKeys.Count == 0)
            return (byte)'F';
        return DecoyVirtualKeys[Random.Shared.Next(DecoyVirtualKeys.Count)];
    }

    public static bool DecoyBeforeMain() =>
        Random.Shared.Next(100) < 40;

    private static HashSet<byte> BuildReserved()
    {
        // Game Options → Shortcut → General (from user screenshot) — never decoy these.
        byte[] gameUi =
        [
            (byte)'W', (byte)'A', (byte)'S', (byte)'D',
            (byte)'T', (byte)'C', (byte)'K', (byte)'N',
            (byte)'U', (byte)'X',
            0x09, // Tab
            (byte)'V', (byte)'M', (byte)'R', (byte)'B',
            (byte)'Y', (byte)'E'
        ];

        var set = new HashSet<byte>(gameUi);

        // Skill panel keys also never used as decoys.
        foreach (var key in ShortcutPanels.AllKeys)
            set.Add(key.VirtualKey);

        byte[] system =
        [
            0x08, 0x0D, 0x10, 0x11, 0x12, 0x14, 0x1B,
            0x20, // Space — letters only for decoys
            0x5B, 0x5C, 0x5D, 0x2C, 0x2D, 0x2E, 0x21, 0x22, 0x13
        ];
        foreach (var vk in system)
            set.Add(vk);

        return set;
    }

    /// <summary>
    /// Letters A–Z only, minus General shortcuts and anything already reserved.
    /// Typically: F G H I J L O P Q Z
    /// </summary>
    private static List<byte> BuildDecoyPool() =>
        Enumerable.Range(0x41, 26)
            .Select(v => (byte)v)
            .Where(vk => !ReservedVirtualKeys.Contains(vk))
            .ToList();
}
