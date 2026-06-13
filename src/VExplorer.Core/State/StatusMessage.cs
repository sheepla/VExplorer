namespace VExplorer.Core.State;

/// <summary>
/// A status-bar message and its severity. Errors are shown emphasised (red);
/// informational messages (e.g. <c>:pwd</c> output) use the default colour.
/// </summary>
public readonly record struct StatusMessage(string Text, bool IsError)
{
    public static StatusMessage None { get; } = new("", false);
}
