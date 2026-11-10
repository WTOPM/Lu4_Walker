namespace WTOPMBot.Models;

public sealed class KeySlotBinding
{
    public int Panel { get; set; }
    public int Index { get; set; }
    public BotAction Action { get; set; }
}

public sealed class KeyBindProfile
{
    public List<KeySlotBinding> Slots { get; set; } = [];

    public static KeyBindProfile CreateDefault()
    {
        var profile = new KeyBindProfile();
        foreach (var key in ShortcutPanels.AllKeys)
        {
            var action = BotAction.None;
            if (key.Panel == 2 && key.Index == 3)
                action = BotAction.SearchMob;
            else if (key.Panel == 2 && key.Index == 1)
                action = BotAction.Attack;

            profile.Slots.Add(new KeySlotBinding
            {
                Panel = key.Panel,
                Index = key.Index,
                Action = action
            });
        }

        return profile;
    }

    public void EnsureComplete()
    {
        var map = Slots.ToDictionary(s => (s.Panel, s.Index));
        foreach (var key in ShortcutPanels.AllKeys)
        {
            if (!map.ContainsKey((key.Panel, key.Index)))
            {
                Slots.Add(new KeySlotBinding
                {
                    Panel = key.Panel,
                    Index = key.Index,
                    Action = BotAction.None
                });
            }
        }

        EnsureSingleNextTarget();
    }

    public BotAction GetAction(int panel, int index)
    {
        var slot = Slots.FirstOrDefault(s => s.Panel == panel && s.Index == index);
        return slot?.Action ?? BotAction.None;
    }

    public void SetAction(int panel, int index, BotAction action)
    {
        if (action == BotAction.NextTarget)
        {
            foreach (var s in Slots)
            {
                if (s.Action == BotAction.NextTarget && (s.Panel != panel || s.Index != index))
                    s.Action = BotAction.None;
            }
        }

        var slot = Slots.FirstOrDefault(s => s.Panel == panel && s.Index == index);
        if (slot is null)
        {
            Slots.Add(new KeySlotBinding { Panel = panel, Index = index, Action = action });
            return;
        }

        slot.Action = action;
    }

    public void EnsureSingleNextTarget()
    {
        var nextSlots = Slots.Where(s => s.Action == BotAction.NextTarget).ToList();
        for (var i = 1; i < nextSlots.Count; i++)
            nextSlots[i].Action = BotAction.None;
    }

    public ShortcutPanels.KeyDef? FindKey(BotAction action)
    {
        if (action == BotAction.None)
            return null;

        foreach (var key in ShortcutPanels.AllKeys)
        {
            if (GetAction(key.Panel, key.Index) == action)
                return key;
        }

        return null;
    }

    /// <summary>All keys bound to action, in panel order (for sequential named targets).</summary>
    public IReadOnlyList<ShortcutPanels.KeyDef> FindAllKeys(BotAction action)
    {
        if (action == BotAction.None)
            return [];

        var list = new List<ShortcutPanels.KeyDef>();
        foreach (var key in ShortcutPanels.AllKeys)
        {
            if (GetAction(key.Panel, key.Index) == action)
                list.Add(key);
        }

        return list;
    }
}
