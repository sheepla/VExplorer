namespace VExplorer.Core.Modes;

public static class ModeStateMachine
{
    public static Mode Transition(Mode current, ModeEvent @event)
    {
        return (current, @event) switch
        {
            // Any mode → submode. Entering a submode from another submode is
            // allowed and discards the previous one; callers (the file list /
            // input bars) reconcile the side effects of leaving the old mode.
            (_, ModeEvent.EnterVisual e) => new Mode.Visual(e.AnchorIndex),
            (_, ModeEvent.EnterSearch) => new Mode.Search("", false),
            (_, ModeEvent.EnterFilter) => new Mode.Filter("", false),
            (_, ModeEvent.EnterCommand) => new Mode.Command(""),
            (_, ModeEvent.EnterAddress) => new Mode.Address(""),
            (_, ModeEvent.OpenMenu) => new Mode.Menu(),

            // ExitToNormal: submode → Normal, Normal → no-op
            (Mode.Normal, ModeEvent.ExitToNormal) => current,
            (_, ModeEvent.ExitToNormal) => new Mode.Normal(),

            // Query updates (Search / Filter)
            (Mode.Search s, ModeEvent.UpdateQuery e) => s with
            {
                Query = e.Query,
                IsConfirmed = false,
            },
            (Mode.Filter f, ModeEvent.UpdateQuery e) => f with
            {
                Query = e.Query,
                IsConfirmed = false,
            },

            // Buffer updates (Command / Address)
            (Mode.Command c, ModeEvent.UpdateBuffer e) => c with { Buffer = e.Buffer },
            (Mode.Address a, ModeEvent.UpdateBuffer e) => a with { Buffer = e.Buffer },

            // ConfirmMode: Search/Filter → mark confirmed (caller reads then ExitToNormal)
            (Mode.Search s, ModeEvent.ConfirmMode) => s with { IsConfirmed = true },
            (Mode.Filter f, ModeEvent.ConfirmMode) => f with { IsConfirmed = true },

            // ConfirmMode: Command/Address/Visual/Menu → Normal
            (Mode.Command, ModeEvent.ConfirmMode) => new Mode.Normal(),
            (Mode.Address, ModeEvent.ConfirmMode) => new Mode.Normal(),
            (Mode.Visual, ModeEvent.ConfirmMode) => new Mode.Normal(),
            (Mode.Menu, ModeEvent.ConfirmMode) => new Mode.Normal(),

            // Illegal combinations
            _ => throw new InvalidOperationException(
                $"Illegal mode transition: {current.GetType().Name} + {{{@event.GetType().Name}}}"
            ),
        };
    }
}
