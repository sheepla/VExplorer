namespace VExplorer.Core.Modes;

/// <summary>
/// A lightweight discriminant of <see cref="Mode"/> for key-binding lookup, where
/// the mode's payload (query, buffer, anchor) is irrelevant. Keeps the binding
/// table comparable and testable without pattern-matching the full record.
/// </summary>
public enum ModeKind
{
    Normal,
    Visual,
    Search,
    Filter,
    Command,
    Address,
    Menu,
}

public static class ModeKinds
{
    public static ModeKind Kind(this Mode mode)
    {
        return mode switch
        {
            Mode.Normal => ModeKind.Normal,
            Mode.Visual => ModeKind.Visual,
            Mode.Search => ModeKind.Search,
            Mode.Filter => ModeKind.Filter,
            Mode.Command => ModeKind.Command,
            Mode.Address => ModeKind.Address,
            Mode.Menu => ModeKind.Menu,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode."),
        };
    }
}
