namespace WTOPMBot.Models;

public sealed class ProcessEntry
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public nint MainWindowHandle { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Title)
            ? $"{Name}  ·  PID {Id}"
            : $"{Name}  ·  PID {Id}  ·  {Title}";

    public override string ToString() => DisplayName;
}
