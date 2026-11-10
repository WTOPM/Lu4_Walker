namespace WTOPMBot.Models;

/// <summary>
/// Three L2-style shortcut panels × 12 keys (from game Options → Shortcut).
/// </summary>
public static class ShortcutPanels
{
    public sealed record KeyDef(int Panel, int Index, byte VirtualKey, string Label);

    public static IReadOnlyList<KeyDef> Panel1 { get; } =
    [
        new(1, 1, 0x70, "F1"),
        new(1, 2, 0x71, "F2"),
        new(1, 3, 0x72, "F3"),
        new(1, 4, 0x73, "F4"),
        new(1, 5, 0x74, "F5"),
        new(1, 6, 0x75, "F6"),
        new(1, 7, 0x76, "F7"),
        new(1, 8, 0x77, "F8"),
        new(1, 9, 0x78, "F9"),
        new(1, 10, 0x79, "F10"),
        new(1, 11, 0x7A, "F11"),
        new(1, 12, 0x7B, "F12")
    ];

    public static IReadOnlyList<KeyDef> Panel2 { get; } =
    [
        new(2, 1, 0x31, "1"),
        new(2, 2, 0x32, "2"),
        new(2, 3, 0x33, "3"),
        new(2, 4, 0x34, "4"),
        new(2, 5, 0x35, "5"),
        new(2, 6, 0x36, "6"),
        new(2, 7, 0x37, "7"),
        new(2, 8, 0x38, "8"),
        new(2, 9, 0x39, "9"),
        new(2, 10, 0x30, "0"),
        new(2, 11, 0xBD, "-"),
        new(2, 12, 0xBB, "=")
    ];

    public static IReadOnlyList<KeyDef> Panel3 { get; } =
    [
        new(3, 1, 0x61, "Num 1"),
        new(3, 2, 0x62, "Num 2"),
        new(3, 3, 0x63, "Num 3"),
        new(3, 4, 0x64, "Num 4"),
        new(3, 5, 0x65, "Num 5"),
        new(3, 6, 0x66, "Num 6"),
        new(3, 7, 0x67, "Num 7"),
        new(3, 8, 0x68, "Num 8"),
        new(3, 9, 0x69, "Num 9"),
        new(3, 10, 0x60, "Num 0"),
        new(3, 11, 0x6F, "Num /"),
        new(3, 12, 0x6A, "Num *")
    ];

    public static IEnumerable<KeyDef> AllKeys =>
        Panel1.Concat(Panel2).Concat(Panel3);

    public static IReadOnlyList<KeyDef> GetPanel(int panel) => panel switch
    {
        1 => Panel1,
        2 => Panel2,
        3 => Panel3,
        _ => throw new ArgumentOutOfRangeException(nameof(panel))
    };
}
