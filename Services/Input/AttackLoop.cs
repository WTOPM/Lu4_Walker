using WTOPMBot.Models;

namespace WTOPMBot.Services.Input;

/// <summary>
/// 3 = no target, 1 = alive target, 2 = dead target (selected + no HP + death pixel).
/// </summary>
public sealed class AttackLoop
{
    private readonly IKeyInput _input;
    private long _nextPressTimestamp;
    private int _pressCount;
    private bool _busy;

    public bool Enabled { get; set; } = true;
    public int IntervalMs { get; set; } = 1000;
    public nint TargetHwnd { get; set; }
    public int PressCount => _pressCount;
    public string InputName => _input.Name;
    public string? LastError => _input.LastError;
    public string LastSkipReason { get; private set; } = string.Empty;
    public string LastKeyLabel { get; private set; } = "—";

    public AttackLoop(IKeyInput input)
    {
        _input = input;
    }

    public void Reset()
    {
        _nextPressTimestamp = 0;
        _pressCount = 0;
        _busy = false;
        LastSkipReason = string.Empty;
        LastKeyLabel = "—";
    }

    public void Tick(CombatState combat)
    {
        if (!Enabled)
        {
            LastSkipReason = "выкл";
            return;
        }

        if (TargetHwnd == nint.Zero)
        {
            LastSkipReason = "нет HWND";
            return;
        }

        var now = Environment.TickCount64;
        if (now < _nextPressTimestamp || _busy)
        {
            LastSkipReason = "ожидание";
            return;
        }

        var vk = combat.SuggestedKey;
        if (vk == 0)
        {
            LastSkipReason = combat.Summary;
            LastKeyLabel = "—";
            return;
        }

        LastKeyLabel = combat.SuggestedKeyLabel;
        LastSkipReason = combat.Summary;
        _busy = true;

        var hwnd = TargetHwnd;

        _ = Task.Run(() =>
        {
            try
            {
                _input.Tap(vk, hwnd);
                Interlocked.Increment(ref _pressCount);
            }
            finally
            {
                var jitter = Random.Shared.Next(-80, 81);
                _nextPressTimestamp = Environment.TickCount64 + Math.Max(400, IntervalMs + jitter);
                _busy = false;
            }
        });
    }
}
