using VExplorer.App.Actions;
using VExplorer.App.Features.Completion;
using VExplorer.Core.Actions;
using VExplorer.Core.Commands;
using VExplorer.Core.Completion;
using VExplorer.Core.Modes;
using VExplorer.Core.State;
using CoreSettings = VExplorer.Core.State.Settings;

namespace VExplorer.App.Features.CommandBar;

/// <summary>
/// Drives the bottom command bar (Vim-style <c>:</c> input). Completes command
/// names and, per command metadata, their arguments (e.g. <c>:cd</c> paths).
/// Editing/cycling behaviour is shared via <see cref="CompletionEditorViewModel"/>;
/// Enter executes the command line and returns to NORMAL.
/// </summary>
public sealed class CommandBarViewModel(
    TabState tabState,
    CompletionEngine engine,
    CommandContextResolver resolver,
    ActionDispatcher dispatcher,
    ICommandHistory commandHistory,
    CoreSettings settings
) : CompletionEditorViewModel(tabState, engine, settings.AddressBarDelayMs)
{
    private readonly CommandContextResolver _resolver = resolver;
    private readonly ActionDispatcher _dispatcher = dispatcher;
    private readonly ICommandHistory _commandHistory = commandHistory;

    // Recall cursor over command history: -1 = not recalling (buffer is the
    // user's own), otherwise an index into the (oldest-first) history list.
    private int _recallIndex = -1;

    protected override bool IsActiveMode(Mode mode)
    {
        return mode is Mode.Command;
    }

    protected override void EnterMode()
    {
        TabState.DispatchModeEvent(new ModeEvent.EnterCommand());
    }

    protected override CompletionContext ResolveContext(
        string buffer,
        int caret,
        string currentDirectory
    )
    {
        return _resolver.Resolve(buffer, caret, currentDirectory);
    }

    // The buffer is the command without the leading ":"; start empty.
    protected override string SeedText()
    {
        return "";
    }

    protected override void OnConfirm(string text)
    {
        ClearCandidates();
        string line = text.Trim();
        if (line.Length == 0)
        {
            TabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
            return;
        }
        _commandHistory.Add(line);

        AppAction? action = CommandParser.Parse(line);
        if (action is null)
        {
            int space = line.IndexOf(' ');
            TabState.SetStatusMessage($"Unknown command: {(space < 0 ? line : line[..space])}");
            TabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
            return;
        }

        string? reseed = _dispatcher.Dispatch(action);
        if (reseed != null)
        {
            // Argument-less :cp/:mv asks for a destination: keep the bar open,
            // re-seeded with "cp "/"mv " so the user types/completes the path.
            SetTextSilently(reseed);
            return;
        }
        TabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
    }

    // Reset the recall cursor whenever the command bar is (re)entered.
    protected override void OnActiveChanged(bool active)
    {
        _recallIndex = -1;
    }

    /// <summary>
    /// Pre-fills the command buffer (e.g. the <c>!</c> shortcut seeds "! "). Must be
    /// called after the bar is active so the seed is not overwritten by the mode's
    /// own empty seed.
    /// </summary>
    public void Prefill(string text)
    {
        SetTextSilently(text);
        TabState.DispatchModeEvent(new ModeEvent.UpdateBuffer(text));
    }

    /// <summary>Recall an older command (Up / Ctrl+P).</summary>
    public void HistoryPrev()
    {
        IReadOnlyList<string> entries = _commandHistory.Entries;
        if (entries.Count == 0)
        {
            return;
        }
        // First press starts just past the newest entry.
        if (_recallIndex == -1)
        {
            _recallIndex = entries.Count;
        }
        if (_recallIndex > 0)
        {
            _recallIndex--;
            SetTextSilently(entries[_recallIndex]);
        }
    }

    /// <summary>Recall a newer command, or return to an empty buffer (Down / Ctrl+N).</summary>
    public void HistoryNext()
    {
        IReadOnlyList<string> entries = _commandHistory.Entries;
        if (_recallIndex == -1 || entries.Count == 0)
        {
            return;
        }
        if (_recallIndex < entries.Count - 1)
        {
            _recallIndex++;
            SetTextSilently(entries[_recallIndex]);
        }
        else
        {
            _recallIndex = -1;
            SetTextSilently("");
        }
    }
}
