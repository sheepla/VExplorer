namespace VExplorer.Core.Modes;

public abstract record ModeEvent
{
    private ModeEvent() { }

    public sealed record EnterVisual(int AnchorIndex) : ModeEvent;

    public sealed record EnterSearch : ModeEvent;

    public sealed record EnterFilter : ModeEvent;

    public sealed record EnterCommand : ModeEvent;

    public sealed record EnterAddress : ModeEvent;

    public sealed record OpenMenu : ModeEvent;

    public sealed record ExitToNormal : ModeEvent;

    public sealed record UpdateQuery(string Query) : ModeEvent;

    public sealed record UpdateBuffer(string Buffer) : ModeEvent;

    public sealed record ConfirmMode : ModeEvent;
}
