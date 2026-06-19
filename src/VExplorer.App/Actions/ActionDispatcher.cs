using System.Windows;
using Microsoft.Extensions.Logging;
using VExplorer.App.Features.FileList;
using VExplorer.App.Features.FileOps;
using VExplorer.App.Features.Tree;
using VExplorer.Core.Actions;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Actions;

/// <summary>
/// The single entry point for every semantic operation. Input routes (keys,
/// mouse, commands, menu) map their event to an <see cref="AppAction"/> and call
/// <see cref="Dispatch"/>; this resolves the active tab, runs shared pre/post
/// steps, and routes the action to its handler. UI-state and clipboard/file
/// operations are handled here against the existing services; command-specific
/// actions are delegated to the attached <see cref="ICommandActionHandler"/>.
/// </summary>
public sealed class ActionDispatcher(
    TabManager tabManager,
    AppState appState,
    FileOpsService fileOps,
    IRecycleBinSource recycleBin,
    IOperationHistory history,
    ILogger<ActionDispatcher> logger
)
{
    private readonly TabManager _tabManager = tabManager;
    private readonly AppState _appState = appState;
    private readonly FileOpsService _fileOps = fileOps;
    private readonly IRecycleBinSource _recycleBin = recycleBin;
    private readonly IOperationHistory _history = history;
    private readonly ILogger<ActionDispatcher> _logger = logger;

    private IViewEffects? _view;
    private ICommandActionHandler? _commandActions;

    /// <summary>Wires the window's view effects. Called once after construction.</summary>
    public void AttachView(IViewEffects view)
    {
        _view = view;
    }

    /// <summary>Wires the command-specific handler. Called once after construction.</summary>
    public void AttachCommandHandler(ICommandActionHandler handler)
    {
        _commandActions = handler;
    }

    /// <summary>
    /// Runs an action against the active tab. Returns a non-null string only for
    /// command actions that re-seed the command bar; null otherwise.
    /// </summary>
    public string? Dispatch(AppAction action)
    {
        TabState tab = _tabManager.GetActiveTabState();

        // Any new action clears a stale error so it does not outlive its producer.
        tab.ClearErrorMessage();

        bool wasVisual = tab.ModeValue is Mode.Visual;
        ActionContext ctx = BuildContext(tab);
        string? reseed = Handle(action, ctx);

        // VISUAL: extend the range as the cursor moves.
        if (action is AppAction.MoveCursor && tab.ModeValue is Mode.Visual visual)
        {
            tab.SetSelection(ctx.List.RangeSelection(visual.AnchorIndex, ctx.List.CursorIndex));
        }

        // Scroll the cursor into view after any move (replaces scattered calls).
        if (action is IMovesCursor)
        {
            if (ctx.Focus == Focus.List)
            {
                _view?.ScrollListToCursor();
            }
            else
            {
                _view?.ScrollTreeToCursor();
            }
        }

        // VISUAL: a file operation leaves the mode afterwards.
        if (wasVisual && action is IExitsVisualMode)
        {
            tab.DispatchModeEvent(new ModeEvent.ConfirmMode());
        }

        return reseed;
    }

    private ActionContext BuildContext(TabState tab)
    {
        FileListViewModel list = _tabManager.GetActiveScopedService<FileListViewModel>();
        TreeViewModel tree = _tabManager.GetActiveScopedService<TreeViewModel>();
        return new ActionContext(
            tab,
            list,
            tree,
            tab.FocusValue,
            _view?.OwnerHwnd ?? 0,
            _view?.PageSize ?? 1,
            _view?.HalfPageSize ?? 1
        );
    }

    private string? Handle(AppAction action, ActionContext ctx)
    {
        TabState tab = ctx.Tab;
        switch (action)
        {
            case AppAction.MoveCursor mc:
                Move(ctx, mc.Move);
                break;
            case AppAction.NavigateInto:
                if (ctx.Focus == Focus.List)
                {
                    ctx.List.NavigateIntoCurrent(tab);
                }
                else
                {
                    ctx.Tree.ExpandSelected();
                }
                break;
            case AppAction.NavigateToParent:
                if (ctx.Focus == Focus.List)
                {
                    ctx.List.NavigateToParent(tab);
                }
                else
                {
                    ctx.Tree.CollapseSelected();
                }
                break;
            case AppAction.ActivateItem:
                if (ctx.Focus == Focus.List)
                {
                    ctx.List.ActivateCurrentItem(tab);
                }
                else
                {
                    _view?.NavigateListToTreeSelection();
                }
                break;
            case AppAction.GoBack:
                tab.GoBack();
                break;
            case AppAction.GoForward:
                tab.GoForward();
                break;
            case AppAction.Reload:
                tab.RequestRefresh();
                break;
            case AppAction.LoadAll:
                ctx.List.LoadAll();
                break;
            case AppAction.SetFocus sf:
                tab.SetFocus(sf.Target);
                break;
            case AppAction.EnterMode em:
                EnterMode(ctx, em);
                break;
            case AppAction.ExitToNormal:
                tab.DispatchModeEvent(new ModeEvent.ExitToNormal());
                break;
            case AppAction.ToggleSelectionAtCursor:
                ToggleSelection(ctx);
                break;
            case AppAction.ClearSelectionOrFilter:
                ClearSelectionOrFilter(ctx);
                break;
            case AppAction.Yank y:
                _fileOps.YankToClipboard(ctx.List, tab, y.Cut);
                break;
            case AppAction.Paste:
                _ = _fileOps.PasteAsync(tab, ctx.OwnerHwnd);
                break;
            case AppAction.Trash:
                _ = _fileOps.TrashAsync(ctx.List, tab, ctx.OwnerHwnd);
                break;
            case AppAction.DeletePermanent:
                DeletePermanent(ctx);
                break;
            case AppAction.CopyPaths { Arguments: null }:
                _fileOps.CopyPathsAsText(ctx.List, tab);
                break;
            case AppAction.Undo:
                _ = _history.UndoAsync();
                break;
            case AppAction.Redo:
                _ = _history.RedoAsync();
                break;
            case AppAction.BeginRename:
                _view?.BeginInlineRename();
                break;
            case AppAction.RestoreFromRecycleBin:
                _recycleBin.Restore(ctx.List.ResolveTargetTokens());
                tab.RequestRefresh();
                break;
            case AppAction.EmptyRecycleBin:
                _recycleBin.Empty(ctx.OwnerHwnd);
                tab.RequestRefresh();
                break;
            case AppAction.NextMatch:
                ctx.List.NextMatch();
                break;
            case AppAction.PrevMatch:
                ctx.List.PrevMatch();
                break;
            case AppAction.EnterSearch { Query: null }:
                _view?.EnterSearch();
                break;
            case AppAction.EnterFilter { Query: null }:
                _view?.EnterFilter();
                break;
            case AppAction.ActivateTabByNumber t:
                _tabManager.ActivateByIndex(TabIndex(t.Number));
                break;
            case AppAction.ActivateRelativeTab r:
                _tabManager.ActivateRelative(r.Delta);
                break;
            case AppAction.MoveTab m:
                _tabManager.MoveActiveTab(m.Delta);
                break;
            case AppAction.NewTab:
                _tabManager.OpenTab();
                break;
            case AppAction.CloseActiveTab:
                CloseActiveTab();
                break;
            case AppAction.MenuMove mm:
                MenuMove(mm.Move);
                break;
            case AppAction.MenuEnterSubmenu:
                _view?.MenuEnterSubmenu();
                break;
            case AppAction.MenuBack:
                _view?.MenuBack();
                break;
            case AppAction.MenuInvoke:
                _view?.MenuInvoke();
                break;
            case AppAction.MenuClose:
                _view?.MenuClose();
                break;
            default:
                // Command-specific actions (cd, zip, properties, …) are run by the
                // attached command handler; see CommandExecutor.
                if (_commandActions is { } handler)
                {
                    return handler.Handle(action, ctx);
                }
                _logger.LogDebug("No handler for action {Action}", action.GetType().Name);
                break;
        }
        return null;
    }

    private static void Move(ActionContext ctx, CursorMove move)
    {
        ICursorTarget t = ctx.CursorTarget;
        switch (move)
        {
            case CursorMove.Down:
                t.MoveCursorDown();
                break;
            case CursorMove.Up:
                t.MoveCursorUp();
                break;
            case CursorMove.Top:
                t.MoveCursorToTop();
                break;
            case CursorMove.Bottom:
                t.MoveCursorToBottom();
                break;
            case CursorMove.HalfPageUp:
                t.MoveCursorPageUp(ctx.HalfPageSize);
                break;
            case CursorMove.HalfPageDown:
                t.MoveCursorPageDown(ctx.HalfPageSize);
                break;
            case CursorMove.PageUp:
                t.MoveCursorPageUp(ctx.PageSize);
                break;
            case CursorMove.PageDown:
                t.MoveCursorPageDown(ctx.PageSize);
                break;
        }
    }

    private void EnterMode(ActionContext ctx, AppAction.EnterMode em)
    {
        TabState tab = ctx.Tab;
        switch (em.Target)
        {
            case ModeTarget.Command:
                tab.DispatchModeEvent(new ModeEvent.EnterCommand());
                _view?.FocusCommandBar(em.Seed);
                break;
            case ModeTarget.Address:
                tab.DispatchModeEvent(new ModeEvent.EnterAddress());
                _view?.FocusAddressBar();
                break;
            case ModeTarget.Visual:
                if (ctx.Focus == Focus.List && ctx.List.CursorRow is { IsParentEntry: false })
                {
                    int anchor = ctx.List.CursorIndex;
                    tab.DispatchModeEvent(new ModeEvent.EnterVisual(anchor));
                    tab.SetSelection(ctx.List.RangeSelection(anchor, anchor));
                }
                break;
            case ModeTarget.Menu:
                _view?.OpenContextMenuAtCursor();
                break;
        }
    }

    private static void ToggleSelection(ActionContext ctx)
    {
        FileItemRow? row = ctx.List.CursorRow;
        if (row is null || row.IsParentEntry)
        {
            return;
        }
        ctx.Tab.SetSelection(ctx.List.ToggleSelection(ctx.Tab.SelectionValue, row.Index));
        ctx.List.MoveCursorDown();
    }

    private static void ClearSelectionOrFilter(ActionContext ctx)
    {
        TabState tab = ctx.Tab;
        if (tab.ModeValue is Mode.Visual)
        {
            tab.SetSelection(System.Collections.Immutable.ImmutableHashSet<int>.Empty);
            tab.DispatchModeEvent(new ModeEvent.ExitToNormal());
        }
        else
        {
            ctx.List.ClearFilter();
        }
    }

    private void DeletePermanent(ActionContext ctx)
    {
        TabState tab = ctx.Tab;
        if (KnownLocations.IsRecycleBin(tab.CurrentLocationValue))
        {
            _recycleBin.DeletePermanently(ctx.List.ResolveTargetTokens(), ctx.OwnerHwnd);
            tab.RequestRefresh();
        }
        else
        {
            _ = _fileOps.DeletePermanentAsync(ctx.List, tab, ctx.OwnerHwnd);
        }
    }

    private void MenuMove(CursorMove move)
    {
        switch (move)
        {
            case CursorMove.Down:
                _view?.MenuMoveDown();
                break;
            case CursorMove.Up:
                _view?.MenuMoveUp();
                break;
            case CursorMove.Top:
                _view?.MenuMoveToFirst();
                break;
            case CursorMove.Bottom:
                _view?.MenuMoveToLast();
                break;
            case CursorMove.PageDown:
                _view?.MenuPageDown();
                break;
            case CursorMove.PageUp:
                _view?.MenuPageUp();
                break;
        }
    }

    private int TabIndex(int number)
    {
        return number switch
        {
            0 => 0,
            9 => _tabManager.TabOrder.Count - 1,
            _ => number - 1,
        };
    }

    private void CloseActiveTab()
    {
        if (!_tabManager.CloseTab(_appState.ActiveTabIdValue))
        {
            Application.Current.Shutdown();
        }
    }
}
