using WTOPMBot.Models;

namespace WTOPMBot.Services.Input;

/// <summary>
/// Attack when target HP is visible.
/// Search: NextTarget × N → named SearchMob → rotate.
/// Approach: while nick-target HP is full, optionally NextTarget every N ms.
/// Loot: when skull detected, press Loot × N with interval — no search/attack.
/// </summary>
public sealed class AttackLoop
{
    private const double FullHpThreshold = 98.0;

    private readonly HumanKeyPlayer _player;
    private long _nextPressTimestamp;
    private long _nextApproachTimestamp;
    private int _pressCount;
    private bool _busy;
    private BotAction _plannedAction = BotAction.None;
    private string _phase = "—";

    private int _namedIndex;
    private int _nextTargetAttempt;
    private bool _hadTarget;
    private bool _lastPressWasNamed;
    private bool _advanceNamedAfterFight;
    private bool _acquiredViaNamed;

    private bool _looting;
    private int _lootAttempt;

    public bool Enabled { get; set; } = true;
    public nint TargetHwnd { get; set; }
    public KeyBindProfile Binds { get; set; } = KeyBindProfile.CreateDefault();
    public int NextTargetAttempts { get; set; } = 3;
    public bool ApproachNextTargetEnabled { get; set; }
    public int ApproachNextTargetIntervalMs { get; set; } = 1500;
    public int LootAttempts { get; set; } = 3;
    public int LootIntervalMs { get; set; } = 800;

    public int PressCount => _pressCount;
    public string InputName => _player.InputName;
    public string? LastError => _player.LastError;
    public BotAction PlannedAction => _plannedAction;
    public string LastKeyLabel { get; private set; } = "—";
    public string Phase => _phase;
    public int NamedIndex => _namedIndex;
    public int NextTargetAttempt => _nextTargetAttempt;
    public int LootAttempt => _lootAttempt;
    public bool AcquiredViaNamed => _acquiredViaNamed;
    public bool IsLooting => _looting;

    public int PauseLeftMs
    {
        get
        {
            if (!_busy && _nextPressTimestamp <= 0)
                return 0;
            var left = (int)(_nextPressTimestamp - Environment.TickCount64);
            return left > 0 ? left : 0;
        }
    }

    public AttackLoop(IKeyInput input)
    {
        _player = new HumanKeyPlayer(input);
    }

    public void Reset()
    {
        _nextPressTimestamp = 0;
        _nextApproachTimestamp = 0;
        _pressCount = 0;
        _busy = false;
        _plannedAction = BotAction.None;
        _phase = "—";
        LastKeyLabel = "—";
        _namedIndex = 0;
        _nextTargetAttempt = 0;
        _hadTarget = false;
        _lastPressWasNamed = false;
        _advanceNamedAfterFight = false;
        _acquiredViaNamed = false;
        _looting = false;
        _lootAttempt = 0;
    }

    public void Tick(CombatState combat)
    {
        if (!Enabled)
        {
            _plannedAction = BotAction.None;
            _phase = "Автодействия выключены";
            return;
        }

        if (TargetHwnd == nint.Zero)
        {
            _plannedAction = BotAction.None;
            _phase = "Нет окна игры";
            return;
        }

        // Corpse skull — loot only, no attack / no new search until attempts done.
        if (combat.TargetDead)
        {
            if (_hadTarget && !_looting)
            {
                // Came from a live fight → after loot, rotate nick.
                if (_acquiredViaNamed || _advanceNamedAfterFight)
                    _advanceNamedAfterFight = true;
            }

            if (!_looting)
            {
                _looting = true;
                _lootAttempt = 0;
            }

            _hadTarget = true;
            HandleLoot();
            return;
        }

        if (_looting)
        {
            FinishLootCycle();
        }

        if (combat.TargetAlive)
        {
            if (!_hadTarget && _lastPressWasNamed)
            {
                _advanceNamedAfterFight = true;
                _acquiredViaNamed = true;
                _nextApproachTimestamp = 0;
            }

            _hadTarget = true;
            HandleHasTarget(combat);
            return;
        }

        if (_hadTarget)
        {
            _hadTarget = false;
            if (_advanceNamedAfterFight)
            {
                AdvanceNamedIndex();
                _advanceNamedAfterFight = false;
            }

            _lastPressWasNamed = false;
            _acquiredViaNamed = false;
            _nextTargetAttempt = 0;
            _nextApproachTimestamp = 0;
        }

        ResolveSearchPress();
    }

    private void FinishLootCycle()
    {
        _looting = false;
        _lootAttempt = 0;
        if (_advanceNamedAfterFight)
        {
            AdvanceNamedIndex();
            _advanceNamedAfterFight = false;
        }

        _lastPressWasNamed = false;
        _acquiredViaNamed = false;
        _nextTargetAttempt = 0;
        _nextApproachTimestamp = 0;
        _hadTarget = false;
    }

    private void HandleLoot()
    {
        var lootKey = Binds.FindKey(BotAction.Loot);
        var max = Math.Clamp(LootAttempts, 1, 20);
        var interval = Math.Clamp(LootIntervalMs, 200, 10000);

        if (lootKey is null)
        {
            _plannedAction = BotAction.Loot;
            LastKeyLabel = "—";
            _phase = "Труп · нет клавиши «Лут» — назначьте в Клавиши";
            return;
        }

        if (_lootAttempt >= max)
        {
            // Attempts exhausted while skull still up — start searching new target.
            FinishLootCycle();
            ResolveSearchPress();
            return;
        }

        _plannedAction = BotAction.Loot;
        var n = _lootAttempt + 1;
        if (TryPress(lootKey, $"Лут {n}/{max}", interval))
            _lootAttempt = n;
        else if (PauseLeftMs > 0)
            _phase = $"Лут {n}/{max} · пауза {PauseLeftMs}мс";
    }

    private void HandleHasTarget(CombatState combat)
    {
        var hp = combat.TargetHp?.FillPercent ?? 0;
        var hpFull = hp >= FullHpThreshold;
        var nextKey = Binds.FindKey(BotAction.NextTarget);
        var interval = Math.Clamp(ApproachNextTargetIntervalMs, 300, 10000);

        // Nick-target approach scan: only while HP is still full.
        if (ApproachNextTargetEnabled && _acquiredViaNamed && hpFull && nextKey is not null)
        {
            var now = Environment.TickCount64;
            // First Next only after the interval — give time to start running to the nick.
            if (_nextApproachTimestamp == 0)
                _nextApproachTimestamp = now + interval;

            if (now >= _nextApproachTimestamp)
            {
                _plannedAction = BotAction.NextTarget;
                if (TryPress(nextKey, $"Дорога·Next (HP {hp:0}%)"))
                {
                    _nextApproachTimestamp = Environment.TickCount64 + interval;
                    return;
                }
            }

            // Between approach Next presses — attack so combat can start in range.
            _plannedAction = BotAction.Attack;
            if (!TryPress(Binds.FindKey(BotAction.Attack), "Атака (подход)"))
            {
                var left = Math.Max(0, _nextApproachTimestamp - Environment.TickCount64);
                if (ApproachNextTargetEnabled && left > 0)
                    _phase = $"Дорога к нику · HP full · Next через {left}мс";
            }

            return;
        }

        // Damaged (or approach off) — fight to the end, never Next-target.
        _plannedAction = BotAction.Attack;
        TryPress(
            Binds.FindKey(BotAction.Attack),
            hpFull ? "Атака" : $"Атака до конца (HP {hp:0}%)");
    }

    private void ResolveSearchPress()
    {
        var namedKeys = Binds.FindAllKeys(BotAction.SearchMob);
        var nextKey = Binds.FindKey(BotAction.NextTarget);
        var maxNext = Math.Clamp(NextTargetAttempts, 0, 20);

        if (namedKeys.Count == 0 && nextKey is null)
        {
            _plannedAction = BotAction.SearchMob;
            _phase = "Нет клавиш «Таргет» / «Next target» — назначьте в Клавиши";
            LastKeyLabel = "—";
            return;
        }

        if (namedKeys.Count > 0 && _namedIndex >= namedKeys.Count)
            _namedIndex = 0;

        if (nextKey is not null && maxNext > 0 && _nextTargetAttempt < maxNext)
        {
            if (_lastPressWasNamed)
            {
                AdvanceNamedIndex();
                _lastPressWasNamed = false;
            }

            _plannedAction = BotAction.NextTarget;
            var n = _nextTargetAttempt + 1;
            if (TryPress(nextKey, $"Next target {n}/{maxNext}"))
            {
                _nextTargetAttempt = n;
                _lastPressWasNamed = false;
            }

            return;
        }

        if (namedKeys.Count > 0)
        {
            if (_lastPressWasNamed && maxNext == 0)
            {
                AdvanceNamedIndex();
                _lastPressWasNamed = false;
                if (_namedIndex >= namedKeys.Count)
                    _namedIndex = 0;
            }

            var named = namedKeys[_namedIndex];
            _plannedAction = BotAction.SearchMob;
            var detail = $"Таргет #{_namedIndex + 1}/{namedKeys.Count} «{named.Label}»";
            if (TryPress(named, detail))
            {
                _lastPressWasNamed = true;
                _nextTargetAttempt = 0;
            }

            return;
        }

        _plannedAction = BotAction.NextTarget;
        _nextTargetAttempt = 0;
        TryPress(nextKey, "Next target");
        _lastPressWasNamed = false;
    }

    private void AdvanceNamedIndex()
    {
        var count = Binds.FindAllKeys(BotAction.SearchMob).Count;
        if (count <= 0)
        {
            _namedIndex = 0;
            return;
        }

        _namedIndex = (_namedIndex + 1) % count;
        _nextTargetAttempt = 0;
    }

    private bool TryPress(ShortcutPanels.KeyDef? key, string detail, int? overrideIntervalMs = null)
    {
        if (key is null)
        {
            LastKeyLabel = "—";
            _phase = $"Нет клавиши — {detail}";
            return false;
        }

        var now = Environment.TickCount64;
        if (_busy)
        {
            _phase = $"Нажимаю «{LastKeyLabel}»…";
            return false;
        }

        if (now < _nextPressTimestamp)
        {
            var sec = PauseLeftMs / 1000.0;
            _phase = $"{detail} · пауза {sec:0.0}с";
            return false;
        }

        LastKeyLabel = key.Label;
        _phase = $"Жму «{key.Label}» — {detail}";
        _busy = true;

        var hwnd = TargetHwnd;
        var vk = key.VirtualKey;
        var label = key.Label;
        var interval = overrideIntervalMs ?? HumanInput.NextActionIntervalMs();

        _ = Task.Run(() =>
        {
            try
            {
                _player.TapAction(vk, hwnd);
                Interlocked.Increment(ref _pressCount);
                if (!string.IsNullOrWhiteSpace(_player.LastError))
                    _phase = $"Ошибка ввода: {_player.LastError}";
            }
            finally
            {
                _nextPressTimestamp = Environment.TickCount64 + interval;
                _busy = false;
                LastKeyLabel = label;
            }
        });

        return true;
    }
}
