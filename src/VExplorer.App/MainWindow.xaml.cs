using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using R3;
using VExplorer.App.Features.FileList;
using VExplorer.App.Features.FileOps;
using VExplorer.App.Features.Menu;
using VExplorer.App.Features.Tree;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Modes;
using VExplorer.Core.State;
using VFocus = VExplorer.Core.Modes.Focus;

namespace VExplorer.App;

public partial class MainWindow : Window
{
    private readonly TabManager _tabManager;
    private readonly AppState _appState;
    private readonly FileOpsService _fileOps;
    private readonly IRecycleBinSource _recycleBin;
    private readonly IOperationHistory _history;
    private readonly ISpecialFolderSource _specialFolders;
    private readonly ShellMenuHost _shellMenu;
    private readonly Features.CommandBar.CommandExecutor _executor;
    private readonly IDisposable _activeTabSubscription;

    /// <summary>The tab VMs the scroll handlers are currently wired to (for re-wiring on switch).</summary>
    private TreeViewModel? _wiredTree;
    private FileListViewModel? _wiredFileList;

    /// <summary>Cached owner window handle for OS file-operation dialogs.</summary>
    private nint _hwnd;

    /// <summary>First key of a pending two-key chord (yy / dd), or null.</summary>
    private Key? _pendingChord;

    /// <summary>Mouse position where a potential drag started (List).</summary>
    private Point _dragStart;
    private bool _maybeDrag;

    /// <summary>The row under the press that armed a potential drag.</summary>
    private FileItemRow? _dragRow;

    public MainWindow(
        MainWindowViewModel viewModel,
        TabManager tabManager,
        AppState appState,
        FileOpsService fileOps,
        IRecycleBinSource recycleBin,
        IOperationHistory history,
        ISpecialFolderSource specialFolders,
        ShellMenuHost shellMenu,
        Features.CommandBar.CommandExecutor executor
    )
    {
        _tabManager = tabManager;
        _appState = appState;
        _fileOps = fileOps;
        _recycleBin = recycleBin;
        _history = history;
        _specialFolders = specialFolders;
        _shellMenu = shellMenu;
        _executor = executor;
        InitializeComponent();
        DataContext = viewModel;

        // Wire the scroll handlers to the active tab, and re-wire on every tab
        // switch (the proxied Tree/FileList VMs are per-tab instances). Also route
        // × close-button requests through the same close path as Ctrl+W.
        RewireScrollHandlers();
        _activeTabSubscription = appState
            .ActiveTabId.ObserveOnCurrentDispatcher()
            .Subscribe(_ =>
            {
                RewireScrollHandlers();
                // Move physical focus into the new tab's list so keys land there.
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() => FileList.Focus())
                );
            });
        viewModel.TabBar.CloseRequested.Subscribe(CloseTab);

        // handledEventsToo:true ensures Tab and arrow keys reach us even after
        // WPF's built-in KeyboardNavigation class handler marks them as Handled.
        AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnWindowKeyDown),
            handledEventsToo: true
        );

        // Mouse back / forward thumb buttons → navigation history.
        AddHandler(
            PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnWindowMouseDown),
            handledEventsToo: true
        );
    }

    /// <summary>
    /// Points the Tree/FileList scroll-into-view handlers at the active tab's VMs,
    /// detaching them from the previously-wired tab. Idempotent.
    /// </summary>
    private void RewireScrollHandlers()
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;

        if (_wiredTree != null)
        {
            _wiredTree.PropertyChanged -= OnTreePropertyChanged;
        }
        if (_wiredFileList != null)
        {
            _wiredFileList.PropertyChanged -= OnFileListPropertyChanged;
        }

        _wiredTree = vm.Tree;
        _wiredFileList = vm.FileList;
        _wiredTree.PropertyChanged += OnTreePropertyChanged;
        _wiredFileList.PropertyChanged += OnFileListPropertyChanged;
    }

    // Keep the Tree's selected node scrolled into view (with margin) however the
    // cursor moves (keys, directory sync, etc.).
    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TreeViewModel.SelectedNode))
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(ScrollTreeToCursor)
            );
        }
    }

    // Keep the cursor row scrolled into view whenever it moves on its own — a
    // SEARCH jump, a FILTER re-narrow, or a file op repositioning by name.
    private void OnFileListPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FileListViewModel.CursorIndex))
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(ScrollFileListToCursor)
            );
        }
    }

    /// <summary>Closes the active tab; closing the last tab exits the app.</summary>
    private void CloseActiveTab()
    {
        CloseTab(_appState.ActiveTabIdValue);
    }

    /// <summary>Closes a specific tab; closing the last one exits the app.</summary>
    private void CloseTab(Guid tabId)
    {
        if (!_tabManager.CloseTab(tabId))
        {
            Application.Current.Shutdown();
        }
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        TabState tabState = _tabManager.GetActiveTabState();
        if (e.ChangedButton == MouseButton.XButton1)
        {
            tabState.GoBack();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            tabState.GoForward();
            e.Handled = true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        TabState tabState = _tabManager.GetActiveTabState();

        // Let focused text inputs (address/command bars, inline rename) type freely.
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        // Any new keyboard action clears a stale error so it doesn't outlive the
        // operation that produced it.
        tabState.ClearErrorMessage();

        // Tab management (Ctrl-based) works in any mode; handled before the
        // NORMAL-only guard. Bracket/brace/digit tab keys are NORMAL-only and
        // live further down / in OnPreviewTextInput.
        {
            bool tabCtrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
            bool tabShift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
            if (tabCtrl && e.Key == Key.T)
            {
                _tabManager.OpenTab();
                e.Handled = true;
                return;
            }
            if (tabCtrl && e.Key == Key.W)
            {
                CloseActiveTab();
                e.Handled = true;
                return;
            }
            if (tabCtrl && e.Key == Key.Tab)
            {
                _tabManager.ActivateRelative(tabShift ? -1 : +1);
                e.Handled = true;
                return;
            }
        }

        // VISUAL mode: range-selection keys (handled before the NORMAL-only guard).
        if (tabState.ModeValue is Mode.Visual visual)
        {
            HandleVisualKey(e, tabState, (MainWindowViewModel)DataContext, visual.AnchorIndex);
            return;
        }

        // MENU mode: hjkl navigation over the context menu (before the NORMAL guard).
        if (tabState.ModeValue is Mode.Menu)
        {
            HandleMenuKey(e, (MainWindowViewModel)DataContext, tabState);
            return;
        }

        // Ctrl+L / F4 — enter ADDRESS mode and focus the address bar. Handled
        // before the NORMAL-only guard; ADDRESS-mode keys live in the address
        // bar's own PreviewKeyDown.
        if (
            tabState.ModeValue is Mode.Normal
            && (
                e.Key == Key.F4
                || (e.Key == Key.L && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
            )
        )
        {
            tabState.DispatchModeEvent(new ModeEvent.EnterAddress());
            // Focus after the TextBox becomes visible (IsActive binding applied).
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => AddressBarControl.FocusInput())
            );
            e.Handled = true;
            return;
        }

        // ":" is handled in OnPreviewTextInput so it works on any keyboard
        // layout (on JIS, ":" is a dedicated key, not Shift+";").

        if (tabState.ModeValue is not Mode.Normal)
        {
            return;
        }

        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        VFocus focus = tabState.FocusValue;
        bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool alt = (e.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0;
        bool noMod = e.KeyboardDevice.Modifiers == ModifierKeys.None;

        // Digit keys jump to a tab by position: 1–8 absolute, 9 last, 0 first.
        // Out-of-range positions are a no-op (handled in ActivateByIndex).
        if (noMod && TryGetDigit(e.Key, out int digit))
        {
            int index = digit switch
            {
                0 => 0,
                9 => _tabManager.TabOrder.Count - 1,
                _ => digit - 1,
            };
            _tabManager.ActivateByIndex(index);
            e.Handled = true;
            return;
        }

        // Two-key chords yy (copy) / dd (trash), List focus only. Any other key
        // clears a pending chord and falls through to normal handling.
        Key? pending = _pendingChord;
        _pendingChord = null;
        if (focus == VFocus.List && noMod && e.Key is Key.Y or Key.D)
        {
            if (e.Key == Key.Y)
            {
                if (pending == Key.Y)
                {
                    _fileOps.YankToClipboard(vm.FileList, tabState, cut: false);
                }
                else
                {
                    _pendingChord = Key.Y;
                }
            }
            else // Key.D
            {
                if (pending == Key.D)
                {
                    // dd — cut (move to clipboard); trash is on "x".
                    _fileOps.YankToClipboard(vm.FileList, tabState, cut: true);
                }
                else
                {
                    _pendingChord = Key.D;
                }
            }
            e.Handled = true;
            return;
        }

        // Folder navigation history (back / forward). Alt+arrow arrives as a
        // system key, so read SystemKey when e.Key is Key.System.
        Key navKey = e.Key == Key.System ? e.SystemKey : e.Key;
        bool goBack =
            (!ctrl && !alt && shift && navKey == Key.OemComma) // <
            || (!ctrl && !shift && alt && navKey == Key.Left); // Alt+Left
        bool goForward =
            (!ctrl && !alt && shift && navKey == Key.OemPeriod) // >
            || (!ctrl && !shift && alt && navKey == Key.Right); // Alt+Right
        if (goBack || goForward)
        {
            if (goBack)
            {
                tabState.GoBack();
            }
            else
            {
                tabState.GoForward();
            }
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.J when noMod:
            case Key.Down when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorDown();
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorDown();
                }
                e.Handled = true;
                break;

            case Key.K when noMod:
            case Key.Up when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorUp();
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorUp();
                }
                e.Handled = true;
                break;

            // ── h / Left — List: parent dir | Tree: collapse / jump to parent ──
            case Key.H when noMod:
            case Key.Left when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.NavigateToParent(tabState);
                }
                else
                {
                    vm.Tree.CollapseSelected();
                }
                e.Handled = true;
                break;

            // ── l / Right — List: enter dir (or `..`) | Tree: expand / step in ──
            case Key.L when noMod:
            case Key.Right when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.NavigateIntoCurrent(tabState);
                }
                else
                {
                    vm.Tree.ExpandSelected();
                }
                e.Handled = true;
                break;

            // ── Enter — open file (List) | navigate FileList to tree selection ──
            case Key.Return when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.ActivateCurrentItem(tabState);
                }
                else
                {
                    NavigateFileListToTreeSelection(tabState);
                }
                e.Handled = true;
                break;

            // ── Backspace — always navigate to parent ─────────────────────
            case Key.Back when noMod:
                vm.FileList.NavigateToParent(tabState);
                e.Handled = true;
                break;

            // ── g — jump to first item ────────────────────────────────────
            case Key.G when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorToTop();
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorToTop();
                }
                e.Handled = true;
                break;

            // ── G (Shift+G) — jump to last item ──────────────────────────
            case Key.G when shift:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorToBottom();
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorToBottom();
                }
                e.Handled = true;
                break;

            // ── Ctrl+U — half-page up ─────────────────────────────────────
            case Key.U when ctrl:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorPageUp(GetHalfPageSize());
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorPageUp(GetHalfPageSize());
                }
                e.Handled = true;
                break;

            // ── Ctrl+D — half-page down ───────────────────────────────────
            case Key.D when ctrl:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorPageDown(GetHalfPageSize());
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorPageDown(GetHalfPageSize());
                }
                e.Handled = true;
                break;

            // ── Ctrl+B / PgUp — full page up ──────────────────────────────
            case Key.B when ctrl:
            case Key.PageUp when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorPageUp(GetPageSize());
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorPageUp(GetPageSize());
                }
                e.Handled = true;
                break;

            // ── PgDn — full page down (Ctrl+F is SEARCH; see below) ───────
            case Key.PageDown when noMod:
                if (focus == VFocus.List)
                {
                    vm.FileList.MoveCursorPageDown(GetPageSize());
                    ScrollFileListToCursor();
                }
                else
                {
                    vm.Tree.MoveCursorPageDown(GetPageSize());
                }
                e.Handled = true;
                break;

            // ── SEARCH (Ctrl+F, also "/") / FILTER (Shift+F) ──────────────
            case Key.F when ctrl:
                EnterSearchOrFilter(tabState, new ModeEvent.EnterSearch());
                e.Handled = true;
                break;
            case Key.F when shift:
                EnterSearchOrFilter(tabState, new ModeEvent.EnterFilter());
                e.Handled = true;
                break;

            // ── n / N — next / previous SEARCH match ──────────────────────
            case Key.N when noMod && focus == VFocus.List:
                vm.FileList.NextMatch();
                ScrollFileListToCursor();
                e.Handled = true;
                break;
            case Key.N when shift && focus == VFocus.List:
                vm.FileList.PrevMatch();
                ScrollFileListToCursor();
                e.Handled = true;
                break;

            // ── Esc — clear an active FILTER (NORMAL) ─────────────────────
            case Key.Escape when noMod && focus == VFocus.List:
                if (vm.FileList.ClearFilter())
                {
                    e.Handled = true;
                }
                break;

            // ── Shift+H / Shift+L — focus left / right ────────────────────
            case Key.H when shift:
                tabState.SetFocus(VFocus.Tree);
                e.Handled = true;
                break;

            case Key.L when shift:
                tabState.SetFocus(VFocus.List);
                e.Handled = true;
                break;

            // ── Shift+Left / Shift+Right — focus left / right (alias) ─────
            case Key.Left when shift:
                tabState.SetFocus(VFocus.Tree);
                e.Handled = true;
                break;

            case Key.Right when shift:
                tabState.SetFocus(VFocus.List);
                e.Handled = true;
                break;

            // ── Space — toggle multi-select at cursor, then step down ─────
            case Key.Space when noMod:
                if (focus == VFocus.List)
                {
                    ToggleSelectionAtCursor(vm, tabState);
                }
                e.Handled = true;
                break;

            // ── v — enter VISUAL (List focus only) ────────────────────────
            case Key.V when noMod:
                if (focus == VFocus.List && vm.FileList.CursorRow is { IsParentEntry: false })
                {
                    int anchor = vm.FileList.CursorIndex;
                    tabState.DispatchModeEvent(new ModeEvent.EnterVisual(anchor));
                    tabState.SetSelection(vm.FileList.RangeSelection(anchor, anchor));
                }
                e.Handled = true;
                break;

            // ── Copy / cut / paste / copy-path ────────────────────────────
            case Key.C when ctrl:
                _fileOps.YankToClipboard(vm.FileList, tabState, cut: false);
                e.Handled = true;
                break;

            case Key.X when ctrl:
                _fileOps.YankToClipboard(vm.FileList, tabState, cut: true);
                e.Handled = true;
                break;

            case Key.V when ctrl:
            case Key.P when noMod:
                _ = _fileOps.PasteAsync(tabState, _hwnd);
                e.Handled = true;
                break;

            case Key.Y when shift:
                _fileOps.CopyPathsAsText(vm.FileList, tabState);
                e.Handled = true;
                break;

            // ── u / Ctrl+Z — undo | Ctrl+R / Ctrl+Y — redo ────────────────
            case Key.U when noMod && focus == VFocus.List:
            case Key.Z when ctrl:
                _ = _history.UndoAsync();
                e.Handled = true;
                break;

            case Key.R when ctrl:
            case Key.Y when ctrl:
                _ = _history.RedoAsync();
                e.Handled = true;
                break;

            // ── x / Delete — trash | Shift+Delete — permanent ─────────────
            // Inside the Recycle Bin, every delete is a permanent delete.
            case Key.Delete when shift:
                if (KnownLocations.IsRecycleBin(tabState.CurrentLocationValue))
                {
                    _recycleBin.DeletePermanently(vm.FileList.ResolveTargetTokens(), _hwnd);
                    tabState.RequestRefresh();
                }
                else
                {
                    _ = _fileOps.DeletePermanentAsync(vm.FileList, tabState, _hwnd);
                }
                e.Handled = true;
                break;

            case Key.X when noMod:
            case Key.Delete when noMod:
                if (KnownLocations.IsRecycleBin(tabState.CurrentLocationValue))
                {
                    _recycleBin.DeletePermanently(vm.FileList.ResolveTargetTokens(), _hwnd);
                    tabState.RequestRefresh();
                }
                else
                {
                    _ = _fileOps.TrashAsync(vm.FileList, tabState, _hwnd);
                }
                e.Handled = true;
                break;

            // ── r — restore the selected items (Recycle Bin only) ─────────
            case Key.R when noMod && KnownLocations.IsRecycleBin(tabState.CurrentLocationValue):
                _recycleBin.Restore(vm.FileList.ResolveTargetTokens());
                tabState.RequestRefresh();
                e.Handled = true;
                break;

            // ── F2 — inline rename ────────────────────────────────────────
            case Key.F2:
                BeginInlineRename(vm);
                e.Handled = true;
                break;

            // ── o — open the context menu (MENU mode) for the cursor/selection ──
            case Key.O when noMod && focus == VFocus.List:
                OpenContextMenuAtCursor(vm, tabState);
                e.Handled = true;
                break;
        }
    }

    // ── Selection / VISUAL helpers ────────────────────────────────────────

    private void ToggleSelectionAtCursor(MainWindowViewModel vm, TabState tabState)
    {
        FileItemRow? row = vm.FileList.CursorRow;
        if (row is null || row.IsParentEntry)
        {
            return;
        }
        tabState.SetSelection(vm.FileList.ToggleSelection(tabState.SelectionValue, row.Index));
        vm.FileList.MoveCursorDown();
        ScrollFileListToCursor();
    }

    private void HandleVisualKey(
        KeyEventArgs e,
        TabState tabState,
        MainWindowViewModel vm,
        int anchor
    )
    {
        bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
        bool noMod = e.KeyboardDevice.Modifiers == ModifierKeys.None;
        bool moved = false;

        switch (e.Key)
        {
            case Key.J when noMod:
            case Key.Down when noMod:
                vm.FileList.MoveCursorDown();
                moved = true;
                break;
            case Key.K when noMod:
            case Key.Up when noMod:
                vm.FileList.MoveCursorUp();
                moved = true;
                break;
            case Key.G when noMod:
                vm.FileList.MoveCursorToTop();
                moved = true;
                break;
            case Key.G when shift:
                vm.FileList.MoveCursorToBottom();
                moved = true;
                break;
            case Key.U when ctrl:
                vm.FileList.MoveCursorPageUp(GetHalfPageSize());
                moved = true;
                break;
            case Key.D when ctrl:
                vm.FileList.MoveCursorPageDown(GetHalfPageSize());
                moved = true;
                break;
            case Key.B when ctrl:
            case Key.PageUp when noMod:
                vm.FileList.MoveCursorPageUp(GetPageSize());
                moved = true;
                break;
            case Key.F when ctrl:
            case Key.PageDown when noMod:
                vm.FileList.MoveCursorPageDown(GetPageSize());
                moved = true;
                break;
            // ── File operations on the range, then back to NORMAL ─────────
            case Key.Y when noMod: // copy
                _fileOps.YankToClipboard(vm.FileList, tabState, cut: false);
                ExitVisual(tabState);
                e.Handled = true;
                return;
            case Key.D when noMod: // cut
                _fileOps.YankToClipboard(vm.FileList, tabState, cut: true);
                ExitVisual(tabState);
                e.Handled = true;
                return;
            case Key.X when noMod: // trash
            case Key.Delete when noMod:
                _ = _fileOps.TrashAsync(vm.FileList, tabState, _hwnd);
                ExitVisual(tabState);
                e.Handled = true;
                return;
            case Key.Delete when shift: // permanent delete
                _ = _fileOps.DeletePermanentAsync(vm.FileList, tabState, _hwnd);
                ExitVisual(tabState);
                e.Handled = true;
                return;
            case Key.Y when shift: // copy paths as text
                _fileOps.CopyPathsAsText(vm.FileList, tabState);
                ExitVisual(tabState);
                e.Handled = true;
                return;

            case Key.Return:
                // Confirm: keep the selection, back to NORMAL.
                tabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
                e.Handled = true;
                return;
            case Key.Escape:
                tabState.SetSelection(System.Collections.Immutable.ImmutableHashSet<int>.Empty);
                tabState.DispatchModeEvent(new ModeEvent.ExitToNormal());
                e.Handled = true;
                return;
        }

        if (moved)
        {
            tabState.SetSelection(vm.FileList.RangeSelection(anchor, vm.FileList.CursorIndex));
            ScrollFileListToCursor();
        }
        e.Handled = true;
    }

    /// <summary>Leaves VISUAL after an operation (keeps selection; refresh clears stale ones).</summary>
    private static void ExitVisual(TabState tabState)
    {
        tabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
    }

    // ── Inline rename ──────────────────────────────────────────────────────

    private void BeginInlineRename(MainWindowViewModel vm)
    {
        FileItemRow? row = vm.FileList.CursorRow;
        if (row is null || row.IsParentEntry)
        {
            return;
        }
        row.EditName = row.Name;
        row.IsEditing = true; // the cell TextBox focuses itself on becoming visible
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FileItemRow row })
        {
            return;
        }
        if (e.Key == Key.Return)
        {
            CommitRename(row);
            FileList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsEditing = false;
            FileList.Focus();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Clicking away cancels the edit (commit happens on Enter).
        if (sender is TextBox { DataContext: FileItemRow row })
        {
            row.IsEditing = false;
        }
    }

    private void CommitRename(FileItemRow row)
    {
        if (!row.IsEditing)
        {
            return;
        }
        row.IsEditing = false;
        string newName = row.EditName.Trim();
        if (newName.Length == 0 || newName == row.Name)
        {
            return;
        }
        _ = _fileOps.RenameAsync(row.FullPath, newName, _tabManager.GetActiveTabState(), _hwnd);
    }

    // ── Context menu (MENU mode) ──────────────────────────────────────────

    /// <summary>Opens the context menu for the cursor/selection (the <c>o</c> key).</summary>
    private void OpenContextMenuAtCursor(MainWindowViewModel vm, TabState tabState)
    {
        OpenContextMenu(vm, tabState, vm.FileList.ResolveTargetPaths(), GetCursorAnchor());
    }

    /// <summary>
    /// Opens the item menu for <paramref name="targets"/> (or the folder-background menu when
    /// empty), entering MENU mode. Self items are built here (where the deps live); the shell
    /// session and anchor go to the view model.
    /// </summary>
    private void OpenContextMenu(
        MainWindowViewModel vm,
        TabState tabState,
        IReadOnlyList<string> targets,
        Point anchor
    )
    {
        if (tabState.ModeValue is not Mode.Normal)
        {
            return;
        }
        bool background = targets.Count == 0;
        List<MenuItemViewModel> self = background
            ? BuildBackgroundMenuItems(vm, tabState)
            : BuildItemMenuItems(vm, tabState);

        // Open instantly with the self items; the (possibly slow) shell items load on a
        // worker thread and are appended when ready — the UI never blocks.
        tabState.DispatchModeEvent(new ModeEvent.OpenMenu());
        vm.ContextMenu.OpenSelfOnly(self, anchor.X, anchor.Y);
        int generation = vm.ContextMenu.Generation;
        string? folder = tabState.CurrentDirectoryPath;
        _ = LoadShellItemsAsync(vm.ContextMenu, targets, background, folder, generation);
    }

    private async Task LoadShellItemsAsync(
        ContextMenuViewModel menu,
        IReadOnlyList<string> targets,
        bool background,
        string? folder,
        int generation
    )
    {
        HostedMenuSession? session = null;
        try
        {
            session = background
                ? (
                    folder is null
                        ? null
                        : await _shellMenu.OpenForFolderBackgroundAsync(folder, _hwnd)
                )
                : await _shellMenu.OpenForItemsAsync(targets, _hwnd);
        }
        catch
        {
            session = null;
        }
        // Back on the UI thread (await resumed on the dispatcher). The VM drops a stale result.
        menu.AppendShellSession(session, generation);
    }

    private List<MenuItemViewModel> BuildItemMenuItems(MainWindowViewModel vm, TabState tab)
    {
        FileListViewModel list = vm.FileList;
        bool inBin = KnownLocations.IsRecycleBin(tab.CurrentLocationValue);
        return
        [
            new() { Text = "Open", SelfAction = () => list.ActivateCurrentItem(tab) },
            new() { Text = "Open With", SelfAction = () => RunCommand(tab, "openwith") },
            MenuItemViewModel.Separator,
            new()
            {
                Text = "Cut",
                SelfAction = () => _fileOps.YankToClipboard(list, tab, cut: true),
            },
            new()
            {
                Text = "Copy",
                SelfAction = () => _fileOps.YankToClipboard(list, tab, cut: false),
            },
            new() { Text = "Paste", SelfAction = () => _ = _fileOps.PasteAsync(tab, _hwnd) },
            new() { Text = "Copy Path", SelfAction = () => _fileOps.CopyPathsAsText(list, tab) },
            MenuItemViewModel.Separator,
            new() { Text = "Rename", SelfAction = () => BeginInlineRename(vm) },
            new() { Text = "Trash", SelfAction = () => TrashTargets(vm, tab, inBin) },
            new() { Text = "Delete", SelfAction = () => DeleteTargets(vm, tab, inBin) },
            MenuItemViewModel.Separator,
            new() { Text = "Zip", SelfAction = () => RunCommand(tab, "zip") },
            new() { Text = "Unzip", SelfAction = () => RunCommand(tab, "unzip") },
            new() { Text = "Properties", SelfAction = () => RunCommand(tab, "properties") },
            MenuItemViewModel.Separator,
            new() { Text = "New File", SelfAction = () => PrefillCommand(tab, "newfile ") },
            new() { Text = "New Folder", SelfAction = () => PrefillCommand(tab, "mkdir ") },
            MenuItemViewModel.Separator,
            new()
            {
                Text = "Pin",
                SelfSubItems =
                [
                    new()
                    {
                        Text = "Pin to Programs",
                        SelfAction = () => RunCommand(tab, "pin programs"),
                    },
                    new()
                    {
                        Text = "Pin to Desktop",
                        SelfAction = () => RunCommand(tab, "pin desktop"),
                    },
                ],
            },
            MenuItemViewModel.Separator,
            new() { Text = "Undo", SelfAction = () => _ = _history.UndoAsync() },
            new() { Text = "Redo", SelfAction = () => _ = _history.RedoAsync() },
        ];
    }

    private List<MenuItemViewModel> BuildBackgroundMenuItems(MainWindowViewModel vm, TabState tab)
    {
        return
        [
            new() { Text = "Paste", SelfAction = () => _ = _fileOps.PasteAsync(tab, _hwnd) },
            new() { Text = "New File", SelfAction = () => PrefillCommand(tab, "newfile ") },
            new() { Text = "New Folder", SelfAction = () => PrefillCommand(tab, "mkdir ") },
            new() { Text = "Copy Path", SelfAction = () => CopyCurrentFolderPath(tab) },
            new() { Text = "Open Terminal", SelfAction = () => RunCommand(tab, "terminal") },
            MenuItemViewModel.Separator,
            new() { Text = "Undo", SelfAction = () => _ = _history.UndoAsync() },
            new() { Text = "Redo", SelfAction = () => _ = _history.RedoAsync() },
        ];
    }

    /// <summary>Runs a COMMAND-mode command (no-arg shell-delegation items reuse the same path).</summary>
    private void RunCommand(TabState tab, string commandLine)
    {
        _executor.Execute(commandLine, tab);
    }

    private void TrashTargets(MainWindowViewModel vm, TabState tab, bool inBin)
    {
        if (inBin)
        {
            _recycleBin.DeletePermanently(vm.FileList.ResolveTargetTokens(), _hwnd);
            tab.RequestRefresh();
        }
        else
        {
            _ = _fileOps.TrashAsync(vm.FileList, tab, _hwnd);
        }
    }

    private void DeleteTargets(MainWindowViewModel vm, TabState tab, bool inBin)
    {
        if (inBin)
        {
            _recycleBin.DeletePermanently(vm.FileList.ResolveTargetTokens(), _hwnd);
            tab.RequestRefresh();
        }
        else
        {
            _ = _fileOps.DeletePermanentAsync(vm.FileList, tab, _hwnd);
        }
    }

    private void PrefillCommand(TabState tab, string seed)
    {
        tab.DispatchModeEvent(new ModeEvent.EnterCommand());
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => CommandBarControl.Prefill(seed))
        );
    }

    private void CopyCurrentFolderPath(TabState tab)
    {
        if (tab.CurrentDirectoryPath is string dir)
        {
            ShellClipboard.SetText(dir);
            tab.SetStatusMessage("Copied folder path", isError: false);
        }
    }

    /// <summary>MENU-mode navigation keys (hjkl / g / G / Ctrl+D/U / Enter / Esc).</summary>
    private void HandleMenuKey(KeyEventArgs e, MainWindowViewModel vm, TabState tabState)
    {
        ContextMenuViewModel menu = vm.ContextMenu;
        bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.D when ctrl:
                menu.PageDown();
                break;
            case Key.U when ctrl:
                menu.PageUp();
                break;
            case Key.J:
            case Key.Down:
                menu.MoveDown();
                break;
            case Key.K:
            case Key.Up:
                menu.MoveUp();
                break;
            case Key.G when shift:
                menu.MoveToLast();
                break;
            case Key.G:
                menu.MoveToFirst();
                break;
            case Key.L:
            case Key.Right:
                _ = menu.EnterSubmenuAsync();
                break;
            case Key.H:
            case Key.Left:
                menu.Back();
                break;
            case Key.Return:
                InvokeMenuSelection(vm, tabState);
                break;
            case Key.Escape:
                CloseContextMenu(tabState);
                break;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Activates the highlighted item. Shell items run via the session then close; self items
    /// close the menu first (back to NORMAL) so actions that switch mode (New File/Folder →
    /// COMMAND) start cleanly.
    /// </summary>
    private void InvokeMenuSelection(MainWindowViewModel vm, TabState tabState)
    {
        MenuItemViewModel? sel = vm.ContextMenu.SelectedItem;
        if (sel is not { IsSelectable: true })
        {
            return;
        }
        if (sel.HasSubmenu)
        {
            _ = vm.ContextMenu.EnterSubmenuAsync();
            return;
        }
        if (sel.IsShell)
        {
            // Post the invoke to the worker, then close (Dispose is queued after it, FIFO).
            _ = vm.ContextMenu.InvokeShellAsync(sel);
            CloseContextMenu(tabState);
        }
        else
        {
            Action? action = sel.SelfAction;
            CloseContextMenu(tabState);
            action?.Invoke();
        }
    }

    /// <summary>Closes the menu popup and returns to NORMAL. Idempotent / re-entrancy safe.</summary>
    private void CloseContextMenu(TabState tabState)
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        bool wasOpen = vm.ContextMenu.IsOpen;
        vm.ContextMenu.Close();
        if (tabState.ModeValue is Mode.Menu)
        {
            tabState.DispatchModeEvent(new ModeEvent.ConfirmMode());
        }
        if (wasOpen)
        {
            FileList.Focus();
        }
    }

    /// <summary>Position (relative to the file list) of the cursor row, for popup placement.</summary>
    private Point GetCursorAnchor()
    {
        int idx = FileList.SelectedIndex;
        if (
            idx >= 0
            && FileList.ItemContainerGenerator.ContainerFromIndex(idx) is FrameworkElement fe
        )
        {
            Point p = fe.TransformToAncestor(FileList).Transform(new Point(0, 0));
            return new Point(p.X + 24, p.Y + fe.ActualHeight);
        }
        return new Point(24, 24);
    }

    /// <summary>
    /// Right-click → context menu. Per spec the target is the selection/cursor (the click
    /// position does not re-decide it); clicking empty space opens the folder-background menu.
    /// </summary>
    private void FileList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        TabState tabState = _tabManager.GetActiveTabState();
        if (tabState.ModeValue is not Mode.Normal)
        {
            return;
        }
        tabState.SetFocus(VFocus.List);
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        FileItemRow? row = FindRow(e.OriginalSource);
        IReadOnlyList<string> targets = row is { IsParentEntry: false }
            ? vm.FileList.ResolveTargetPaths()
            : [];
        OpenContextMenu(vm, tabState, targets, e.GetPosition(FileList));
        e.Handled = true;
    }

    private void ContextMenuPopup_Closed(object? sender, EventArgs e)
    {
        // Covers click-away (StaysOpen=false); programmatic closes are idempotent.
        CloseContextMenu(_tabManager.GetActiveTabState());
    }

    private void ContextMenuList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        TabState tabState = _tabManager.GetActiveTabState();
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        // Only act on the active (right-most) column; the ListBox already moved its selection.
        if (
            tabState.ModeValue is Mode.Menu
            && sender is FrameworkElement { DataContext: MenuColumnViewModel col }
            && vm.ContextMenu.Columns.Count > 0
            && ReferenceEquals(col, vm.ContextMenu.Columns[^1])
        )
        {
            InvokeMenuSelection(vm, tabState);
        }
    }

    // Keep the highlighted item in view (a programmatic SelectedIndex does not auto-scroll).
    private void ContextMenuList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is { } item)
        {
            list.ScrollIntoView(item);
        }
    }

    /// <summary>
    /// Enters COMMAND mode on ":". Uses text input (not a physical key) so it
    /// works on any keyboard layout — on JIS ":" is a dedicated key, not Shift+";".
    /// </summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);

        TabState tabState = _tabManager.GetActiveTabState();
        if (tabState.ModeValue is Mode.Normal && e.Text == ":")
        {
            tabState.DispatchModeEvent(new ModeEvent.EnterCommand());
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => CommandBarControl.FocusInput())
            );
            e.Handled = true;
        }
        else if (tabState.ModeValue is Mode.Normal && e.Text == "!")
        {
            // "!" enters COMMAND mode pre-seeded with "! " (external-program shortcut).
            tabState.DispatchModeEvent(new ModeEvent.EnterCommand());
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => CommandBarControl.Prefill("! "))
            );
            e.Handled = true;
        }
        else if (tabState.ModeValue is Mode.Normal && e.Text == "/")
        {
            EnterSearchOrFilter(tabState, new ModeEvent.EnterSearch());
            e.Handled = true;
        }
        // Tab switch ([ ]) and reorder ({ }). Character-based for layout
        // independence; skipped while a text box (e.g. inline rename) has focus.
        else if (tabState.ModeValue is Mode.Normal && Keyboard.FocusedElement is not TextBox)
        {
            switch (e.Text)
            {
                case "]":
                    _tabManager.ActivateRelative(+1);
                    e.Handled = true;
                    break;
                case "[":
                    _tabManager.ActivateRelative(-1);
                    e.Handled = true;
                    break;
                case "}":
                    _tabManager.MoveActiveTab(+1);
                    e.Handled = true;
                    break;
                case "{":
                    _tabManager.MoveActiveTab(-1);
                    e.Handled = true;
                    break;
            }
        }
    }

    /// <summary>Maps a digit key (top row or numpad) to 0–9; false for non-digits.</summary>
    private static bool TryGetDigit(Key key, out int digit)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            digit = key - Key.D0;
            return true;
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            digit = key - Key.NumPad0;
            return true;
        }
        digit = 0;
        return false;
    }

    /// <summary>Enters SEARCH or FILTER and focuses the query bar.</summary>
    private void EnterSearchOrFilter(TabState tabState, ModeEvent enter)
    {
        tabState.DispatchModeEvent(enter);
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => SearchFilterBarControl.FocusInput())
        );
    }

    // ── Address-bar toolbar buttons (delegate to the same paths as the keys) ──

    private void NavBack_Click(object sender, RoutedEventArgs e)
    {
        _tabManager.GetActiveTabState().GoBack();
    }

    private void NavForward_Click(object sender, RoutedEventArgs e)
    {
        _tabManager.GetActiveTabState().GoForward();
    }

    private void NavParent_Click(object sender, RoutedEventArgs e)
    {
        ((MainWindowViewModel)DataContext).FileList.NavigateToParent(
            _tabManager.GetActiveTabState()
        );
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        _tabManager
            .GetActiveTabState()
            .NavigateTo(Location.ForPath(_specialFolders.GetHomeDirectory()));
    }

    private void NavReload_Click(object sender, RoutedEventArgs e)
    {
        _tabManager.GetActiveTabState().RequestRefresh();
    }

    private void OpenFilter_Click(object sender, RoutedEventArgs e)
    {
        EnterSearchOrFilter(_tabManager.GetActiveTabState(), new ModeEvent.EnterFilter());
    }

    private void OpenSearch_Click(object sender, RoutedEventArgs e)
    {
        EnterSearchOrFilter(_tabManager.GetActiveTabState(), new ModeEvent.EnterSearch());
    }

    // ── Mouse handlers ────────────────────────────────────────────────────

    /// <summary>Single click on Tree — switch focus to Tree panel (and leave ADDRESS).</summary>
    private void FileTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        TabState tabState = _tabManager.GetActiveTabState();
        ExitAddressIfEditing(tabState);
        tabState.SetFocus(VFocus.Tree);
    }

    /// <summary>
    /// Double-click on Tree — navigate FileList to the selected directory.
    /// Clicks on the expand toggle (ToggleButton / Arrow) are ignored so that
    /// double-clicking the expander does not accidentally navigate.
    /// </summary>
    private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore if the click originated on an element that is not bound to a node.
        if (e.OriginalSource is not FrameworkElement fe)
        {
            return;
        }
        if (fe.DataContext is not TreeNodeViewModel node)
        {
            return;
        }
        // The "… (N more)" row loads the rest of its folder instead of navigating.
        if (node.IsOverflowSentinel)
        {
            _ = node.OverflowParent?.RevealAllChildrenAsync();
            return;
        }
        NavigateFileListToTreeSelection(_tabManager.GetActiveTabState());
    }

    /// <summary>
    /// Single click on List — focus the panel and apply Explorer-like multi-select:
    /// plain click clears the multi-selection, Ctrl+click toggles the clicked row,
    /// Shift+click selects the range from the cursor. The plain click still sets the
    /// cursor via the SelectedIndex binding.
    /// </summary>
    private void FileList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        TabState tabState = _tabManager.GetActiveTabState();
        ExitAddressIfEditing(tabState);
        tabState.SetFocus(VFocus.List);

        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        FileItemRow? row = FindRow(e.OriginalSource);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && row is { IsParentEntry: false })
        {
            tabState.SetSelection(vm.FileList.ToggleSelection(tabState.SelectionValue, row.Index));
        }
        else if (shift && row is { IsParentEntry: false })
        {
            tabState.SetSelection(vm.FileList.RangeSelection(vm.FileList.CursorIndex, row.Index));
        }
        else
        {
            tabState.SetSelection(System.Collections.Immutable.ImmutableHashSet<int>.Empty);
        }

        // Arm a potential drag from a real item (not "..").
        _maybeDrag = e.LeftButton == MouseButtonState.Pressed && row is { IsParentEntry: false };
        _dragRow = row;
        _dragStart = e.GetPosition(null);
    }

    // ── Drag & drop ────────────────────────────────────────────────────────

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_maybeDrag || e.LeftButton != MouseButtonState.Pressed)
        {
            _maybeDrag = false;
            return;
        }

        Point pos = e.GetPosition(null);
        if (
            Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance
        )
        {
            return;
        }

        _maybeDrag = false;
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;

        // Drag the multi-selection when the grabbed row is part of it; otherwise
        // drag just that row (Explorer behaviour).
        TabState tab = _tabManager.GetActiveTabState();
        IReadOnlyList<string> paths =
            _dragRow is { IsParentEntry: false } grabbed
            && !tab.SelectionValue.Contains(grabbed.Index)
                ? [grabbed.FullPath]
                : vm.FileList.ResolveTargetPaths();
        if (paths.Count == 0)
        {
            return;
        }
        DataObject data = ShellClipboard.BuildDataObject(paths);
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        e.Effects = hasFiles
            ? (copy ? DragDropEffects.Copy : DragDropEffects.Move)
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        FileItemRow? row = FindRow(e.OriginalSource);
        TabState tabState = _tabManager.GetActiveTabState();
        // Drop target: a directory row's filesystem path, else the current folder.
        string? targetDir =
            row is { IsParentEntry: false, IsDirectory: true }
            && row.Location.TryGetFileSystemPath(out string rowPath)
                ? rowPath
                : tabState.CurrentDirectoryPath;
        if (targetDir is null)
        {
            return;
        }
        PerformDrop(e, targetDir, tabState);
    }

    private void FileTree_Drop(object sender, DragEventArgs e)
    {
        TreeNodeViewModel? node = FindDataContext<TreeNodeViewModel>(e.OriginalSource);
        if (node is null || string.IsNullOrEmpty(node.FullPath) || !Directory.Exists(node.FullPath))
        {
            return;
        }
        PerformDrop(e, node.FullPath, _tabManager.GetActiveTabState());
    }

    private void PerformDrop(DragEventArgs e, string targetDir, TabState tabState)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }
        string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

        // Skip no-op moves (dropping items back into their own parent directory).
        string target = targetDir.TrimEnd(Path.DirectorySeparatorChar);
        List<string> filtered = [];
        foreach (string p in paths)
        {
            string? parent = Path.GetDirectoryName(p.TrimEnd(Path.DirectorySeparatorChar));
            bool sameDir =
                !copy
                && parent != null
                && string.Equals(parent, target, StringComparison.OrdinalIgnoreCase);
            bool intoSelf = string.Equals(
                p.TrimEnd(Path.DirectorySeparatorChar),
                target,
                StringComparison.OrdinalIgnoreCase
            );
            if (!sameDir && !intoSelf)
            {
                filtered.Add(p);
            }
        }
        if (filtered.Count == 0)
        {
            return;
        }
        _ = _fileOps.DropAsync(filtered, targetDir, copy, _hwnd, tabState);
        e.Handled = true;
    }

    private static T? FindDataContext<T>(object source)
        where T : class
    {
        DependencyObject? d = source as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.DataContext is T match)
            {
                return match;
            }
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Walks up the visual tree from a click source to the bound FileItemRow.</summary>
    private static FileItemRow? FindRow(object source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement { DataContext: FileItemRow row })
            {
                return row;
            }
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Leaves ADDRESS/COMMAND mode when a panel is clicked, keeping mode in sync with focus.</summary>
    private static void ExitAddressIfEditing(TabState tabState)
    {
        if (tabState.ModeValue is Mode.Address or Mode.Command)
        {
            tabState.DispatchModeEvent(new ModeEvent.ExitToNormal());
        }
    }

    /// <summary>
    /// Double-click on List — enter directory or open file (same as Enter / l).
    /// Explicitly syncs SelectedIndex → CursorIndex before activating so that
    /// the click and the activation always refer to the same row.
    /// </summary>
    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        TabState tabState = _tabManager.GetActiveTabState();
        int idx = FileList.SelectedIndex;
        if (idx < 0)
        {
            return;
        }
        vm.FileList.CursorIndex = idx;
        vm.FileList.ActivateCurrentItem(tabState);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates the FileList to the directory currently selected in the Tree,
    /// without moving keyboard focus to the List panel.
    /// </summary>
    private void NavigateFileListToTreeSelection(TabState tabState)
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        TreeNodeViewModel? node = vm.Tree.SelectedNode;
        if (node == null || node.IsPlaceholder)
        {
            return;
        }
        // The "… (N more)" row loads the rest of its folder instead of navigating.
        if (node.IsOverflowSentinel)
        {
            _ = node.OverflowParent?.RevealAllChildrenAsync();
            return;
        }
        tabState.NavigateTo(node.Location);
    }

    /// <summary>Rows of context kept above/below the cursor when auto-scrolling.</summary>
    private const int ScrollMargin = 3;

    /// <summary>
    /// Scrolls the ListView so the cursor row plus ~<see cref="ScrollMargin"/>
    /// rows of context above and below are visible. Bringing the margin rows into
    /// view first, then the cursor, leaves breathing room at the edges.
    /// </summary>
    private void ScrollFileListToCursor()
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        int count = FileList.Items.Count;
        int idx = vm.FileList.CursorIndex;
        if (idx < 0 || idx >= count)
        {
            return;
        }
        FileList.ScrollIntoView(FileList.Items[Math.Min(count - 1, idx + ScrollMargin)]);
        FileList.ScrollIntoView(FileList.Items[Math.Max(0, idx - ScrollMargin)]);
        FileList.ScrollIntoView(FileList.Items[idx]);
    }

    /// <summary>
    /// Brings the Tree's selected node into view with ~<see cref="ScrollMargin"/>
    /// rows of context. Relies on tree virtualization being disabled so neighbour
    /// containers exist.
    /// </summary>
    private void ScrollTreeToCursor()
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        IReadOnlyList<Features.Tree.TreeNodeViewModel> visible = vm.Tree.VisibleNodes;
        Features.Tree.TreeNodeViewModel? selected = vm.Tree.SelectedNode;
        if (selected is null || visible.Count == 0)
        {
            return;
        }
        int idx = -1;
        for (int i = 0; i < visible.Count; i++)
        {
            if (visible[i] == selected)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            return;
        }
        BringTreeNodeIntoView(visible[Math.Min(visible.Count - 1, idx + ScrollMargin)]);
        BringTreeNodeIntoView(visible[Math.Max(0, idx - ScrollMargin)]);
        BringTreeNodeIntoView(selected);
    }

    private void BringTreeNodeIntoView(object node)
    {
        if (FindTreeViewItem(FileTree, node) is { } item)
        {
            item.BringIntoView();
        }
    }

    /// <summary>Recursively locates the realized TreeViewItem for a data item.</summary>
    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }
        foreach (object child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childItem)
            {
                TreeViewItem? found = FindTreeViewItem(childItem, item);
                if (found != null)
                {
                    return found;
                }
            }
        }
        return null;
    }

    /// <summary>Half the visible row count (Ctrl+U / Ctrl+D), min 1.</summary>
    private int GetHalfPageSize()
    {
        return Math.Max(1, GetPageSize() / 2);
    }

    /// <summary>Full visible row count (Ctrl+F / Ctrl+B / PgDn / PgUp), min 1.</summary>
    private int GetPageSize()
    {
        const double estimatedItemHeight = 20.0;
        return Math.Max(1, (int)(FileList.ActualHeight / estimatedItemHeight));
    }

    // ── Column sort ───────────────────────────────────────────────────────

    /// <summary>
    /// Handles a click on any GridViewColumnHeader inside the FileList.
    /// Delegates sorting to <see cref="FileListViewModel.SortBy"/> and
    /// updates the header labels with ↑/↓ arrow indicators.
    /// </summary>
    private void FileList_ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header)
        {
            return;
        }
        if (header.Tag is not string column)
        {
            return;
        }

        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        vm.FileList.SortBy(column);
        UpdateColumnHeaders(vm.FileList.SortColumn, vm.FileList.SortDescending);
    }

    /// <summary>
    /// Updates all GridViewColumnHeader labels to show a ↑ or ↓ on the active
    /// sort column and plain text on the others.
    /// </summary>
    private void UpdateColumnHeaders(string sortColumn, bool descending)
    {
        string arrow = descending ? " ↓" : " ↑";
        foreach (GridViewColumn col in ((GridView)FileList.View).Columns)
        {
            if (col.Header is not GridViewColumnHeader header)
            {
                continue;
            }
            if (header.Tag is not string tag)
            {
                continue;
            }
            header.Content = tag == sortColumn ? tag + arrow : tag;
        }
    }

    private void FileTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e
    )
    {
        MainWindowViewModel vm = (MainWindowViewModel)DataContext;
        if (e.OldValue is TreeNodeViewModel old)
        {
            old.IsSelected = false;
        }
        if (e.NewValue is TreeNodeViewModel node)
        {
            vm.Tree.SelectedNode = node;
            node.IsSelected = true;
        }
    }
}
