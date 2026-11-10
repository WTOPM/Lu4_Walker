namespace WTOPMBot.Models;

public enum BotAction
{
    None = 0,
    SearchMob = 1,
    Attack = 2,
    Loot = 3,
    /// <summary>Nearest / next target. Only one key may be bound.</summary>
    NextTarget = 4
}

public static class BotActionInfo
{
    public static string DisplayName(BotAction action) => action switch
    {
        BotAction.None => "—",
        BotAction.SearchMob => "Таргет (ник)",
        BotAction.Attack => "Атака",
        BotAction.Loot => "Лут",
        BotAction.NextTarget => "Next target",
        _ => action.ToString()
    };

    public static IReadOnlyList<BotAction> Assignable { get; } =
    [
        BotAction.None,
        BotAction.SearchMob,
        BotAction.Attack,
        BotAction.Loot,
        BotAction.NextTarget
    ];
}
