namespace VExplorer.Core.Modes;

public abstract record Mode
{
    private Mode() { }

    public sealed record Normal : Mode;

    public sealed record Visual(int AnchorIndex) : Mode;

    public sealed record Search(string Query, bool IsConfirmed) : Mode;

    public sealed record Filter(string Query, bool IsConfirmed) : Mode;

    public sealed record Command(string Buffer) : Mode;

    public sealed record Address(string Buffer) : Mode;

    public sealed record Menu : Mode;
}
