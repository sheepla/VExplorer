namespace VExplorer.Core.State;

/// <summary>
/// Severity of a status-bar message, controlling its emphasis. <see cref="Info"/>
/// uses the default colour, <see cref="Warning"/> marks a recoverable user-input
/// or environment issue, and <see cref="Error"/> marks a failure shown in red.
/// </summary>
public enum StatusSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A status-bar message and its severity. Errors are shown emphasised (red);
/// warnings are highlighted; informational messages (e.g. <c>:pwd</c> output) use
/// the default colour.
/// </summary>
public readonly record struct StatusMessage(string Text, StatusSeverity Severity)
{
    public static StatusMessage None { get; } = new("", StatusSeverity.Info);

    public bool IsError => Severity == StatusSeverity.Error;
}
