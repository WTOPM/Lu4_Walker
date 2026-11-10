namespace WTOPMBot.Services.Input;

public interface IKeyInput : IDisposable
{
    string Name { get; }
    bool IsReady { get; }
    string? LastError { get; }

    /// <summary>Tap a virtual-key. holdMs overrides human default when supported.</summary>
    void Tap(byte virtualKey, nint targetHwnd, int? holdMs = null);

    /// <summary>Left or right mouse click via hardware when supported.</summary>
    void ClickMouse(bool leftButton, nint targetHwnd, int? holdMs = null);

    /// <summary>Small relative mouse move (anti-pattern noise).</summary>
    void NudgeMouse(int dx, int dy, nint targetHwnd);
}
