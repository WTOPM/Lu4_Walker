namespace WTOPMBot.Services.Input;

/// <summary>
/// Main action key (from bot key binds) + optional letter decoys. No mouse.
/// </summary>
public sealed class HumanKeyPlayer
{
    private readonly IKeyInput _input;

    public HumanKeyPlayer(IKeyInput input)
    {
        _input = input;
    }

    public string InputName => _input.Name;
    public string? LastError => _input.LastError;

    public void TapAction(byte virtualKey, nint targetHwnd)
    {
        var decoyCount = HumanInput.NextDecoyCount();
        var decoysBefore = 0;
        for (var i = 0; i < decoyCount; i++)
        {
            if (HumanInput.DecoyBeforeMain())
                decoysBefore++;
        }

        var decoysAfter = decoyCount - decoysBefore;

        for (var i = 0; i < decoysBefore; i++)
        {
            _input.Tap(HumanInput.PickDecoyVirtualKey(), targetHwnd);
            Thread.Sleep(HumanInput.NextDecoyGapMs());
        }

        // Main skill key from the bot's 3×12 panel bindings.
        _input.Tap(virtualKey, targetHwnd, HumanInput.NextKeyHoldMs());

        for (var i = 0; i < decoysAfter; i++)
        {
            Thread.Sleep(HumanInput.NextDecoyGapMs());
            _input.Tap(HumanInput.PickDecoyVirtualKey(), targetHwnd);
        }
    }
}
