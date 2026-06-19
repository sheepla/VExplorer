using VExplorer.Core.Modes;

namespace VExplorer.Core.Actions;

public enum CursorMove
{
    Up,
    Down,
    Top,
    Bottom,
    HalfPageUp,
    HalfPageDown,
    PageUp,
    PageDown,
}

/// <summary>The submode an <see cref="AppAction.EnterMode"/> requests entry to.</summary>
public enum ModeTarget
{
    Command,
    Address,
    Visual,
    Menu,
}

/// <summary>
/// Marks actions that move the list/tree cursor, so the dispatcher can run the
/// shared "scroll cursor into view" post-step centrally instead of each call site
/// remembering to.
/// </summary>
public interface IMovesCursor;

/// <summary>
/// Marks file operations that, when triggered from VISUAL mode, return to NORMAL
/// afterwards (the dispatcher runs the exit centrally). Harmless in NORMAL.
/// </summary>
public interface IExitsVisualMode;

/// <summary>
/// A semantic operation, decoupled from the input route that produced it. Every
/// input route (keys, mouse, <c>:</c> commands, context menu) maps its raw event
/// to one of these and hands it to the dispatcher, so a given operation has a
/// single implementation regardless of how it was triggered. Mirrors the
/// operation × route table in <c>ActionMatrix</c>.
/// </summary>
public abstract record AppAction
{
    private AppAction() { }

    // Navigation / cursor (target pane resolved by the handler from focus).
    public sealed record MoveCursor(CursorMove Move) : AppAction, IMovesCursor;

    public sealed record NavigateInto : AppAction, IMovesCursor;

    public sealed record NavigateToParent : AppAction, IMovesCursor;

    public sealed record ActivateItem : AppAction;

    public sealed record GoBack : AppAction;

    public sealed record GoForward : AppAction;

    public sealed record Reload : AppAction;

    public sealed record LoadAll : AppAction;

    public sealed record ChangeDirectory(string Argument) : AppAction;

    public sealed record ShowPath : AppAction;

    public sealed record GoToSpecialFolder(string Argument) : AppAction;

    public sealed record GoToHistory(string Argument) : AppAction;

    // Focus / panes / mode.
    public sealed record SetFocus(Focus Target) : AppAction;

    public sealed record TogglePreview : AppAction;

    public sealed record EnterMode(ModeTarget Target, string? Seed = null) : AppAction;

    public sealed record ExitToNormal : AppAction;

    // Selection.
    public sealed record ToggleSelectionAtCursor : AppAction, IMovesCursor;

    public sealed record ClearSelectionOrFilter : AppAction;

    // Clipboard / file operations (implicit target = selection else cursor).
    public sealed record Yank(bool Cut) : AppAction, IExitsVisualMode;

    public sealed record Paste : AppAction;

    public sealed record Trash : AppAction, IExitsVisualMode;

    public sealed record DeletePermanent : AppAction, IExitsVisualMode;

    // A null Arguments is the Y key / menu (selection else cursor); a non-null
    // value is :clippath PATH... (copy the given paths as text).
    public sealed record CopyPaths(string? Arguments = null) : AppAction, IExitsVisualMode;

    public sealed record Undo : AppAction;

    public sealed record Redo : AppAction;

    public sealed record BeginRename : AppAction;

    public sealed record RenameTo(string NewName) : AppAction;

    public sealed record CopyMove(string Arguments, bool Move) : AppAction;

    public sealed record MakeDir(string Argument) : AppAction;

    public sealed record NewFile(string Argument, bool MakeParents) : AppAction;

    public sealed record DropItems(IReadOnlyList<string> Sources, string TargetDirectory, bool Copy)
        : AppAction;

    // Recycle Bin.
    public sealed record RestoreFromRecycleBin : AppAction;

    public sealed record EmptyRecycleBin : AppAction;

    // Shell delegation.
    public sealed record ShowProperties(string Arguments) : AppAction;

    public sealed record OpenWith(string Arguments) : AppAction;

    public sealed record MakeShortcut(string Arguments) : AppAction;

    public sealed record Pin(string Argument) : AppAction;

    public sealed record Zip(string Argument) : AppAction;

    public sealed record Unzip(string Argument) : AppAction;

    public sealed record OpenTerminal(string Argument) : AppAction;

    public sealed record RunExternal(string CommandLine) : AppAction;

    // Search / filter / settings / help. A null query enters the incremental bar;
    // a non-null query runs immediately (the :search / :filter KEYWORD form).
    public sealed record EnterSearch(string? Query) : AppAction;

    public sealed record EnterFilter(string? Query) : AppAction;

    public sealed record NextMatch : AppAction, IMovesCursor;

    public sealed record PrevMatch : AppAction, IMovesCursor;

    public sealed record SetOption(string Arguments) : AppAction;

    public sealed record ShowHelp(string Topic) : AppAction;

    public sealed record SortByColumn(string Column) : AppAction;

    // Tabs.
    public sealed record ActivateTabByNumber(int Number) : AppAction;

    public sealed record ActivateRelativeTab(int Delta) : AppAction;

    public sealed record MoveTab(int Delta) : AppAction;

    public sealed record NewTab : AppAction;

    public sealed record CloseActiveTab : AppAction;

    // Context-menu navigation (MENU mode). CursorMove reuses Down/Up/Top/Bottom
    // for j/k/g/G and PageUp/PageDown for Ctrl+U/Ctrl+D.
    public sealed record MenuMove(CursorMove Move) : AppAction;

    public sealed record MenuEnterSubmenu : AppAction;

    public sealed record MenuBack : AppAction;

    public sealed record MenuInvoke : AppAction;

    public sealed record MenuClose : AppAction;
}
