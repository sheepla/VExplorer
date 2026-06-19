using VExplorer.Core.Actions;

namespace VExplorer.App.Actions;

/// <summary>
/// Handles the command-specific actions (those produced by the <c>:</c> command
/// line and the shell-delegation menu items) that the core dispatcher routes to
/// it. Implemented by the command layer and attached to the dispatcher.
/// </summary>
public interface ICommandActionHandler
{
    /// <summary>
    /// Runs a command-specific action. Returns a non-null string to re-seed the
    /// command bar (argument-less <c>:cp</c>/<c>:mv</c>), otherwise null.
    /// </summary>
    string? Handle(AppAction action, ActionContext context);
}
