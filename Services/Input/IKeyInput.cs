namespace WTOPMBot.Services.Input;

public interface IKeyInput : IDisposable
{
    string Name { get; }
    bool IsReady { get; }
    string? LastError { get; }

    /// <param name="targetHwnd">Game window — focused before SendInput so keys don't hit the bot UI.</param>
    void Tap(byte virtualKey, nint targetHwnd);
}
