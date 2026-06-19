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
using VExplorer.App.Input;
using VExplorer.Core.Actions;
using VExplorer.Core.Commands;
using VExplorer.Core.FileSystem;
using VExplorer.Core.Input;
using VExplorer.Core.Modes;
using VExplorer.Core.State;
using VFocus = VExplorer.Core.Modes.Focus;

namespace VExplorer.App;

public partial class MainWindow : Window
{
    private readonly TabManager _tabManager;
    private readonly AppState _appState;
    private readonly FileOpsService _fileOps;
    private readonly ISpecialFolderSource _specialFolders;
    private readonly ShellMenuHost _shellMenu;
    private readonly Actions.ActionDispatcher _dispatcher;
    private readonly IDisposable _activeTabSubscription;

    /// <summary>The tab VMs the scroll handlers are currently wired to (for re-wiring on switch).</summary>
    private TreeViewModel? _wiredTree;
    private FileListViewModel? _wiredFileList;

    /// <summary>Cached owner window handle for OS file-operation dialogs.</summary>
    private nint _hwnd;

    /// <summary>Folds the raw key stream into chords (yy / dd) before binding lookup.</summary>
    private readonly ChordResolver _chordResolver = new();

    /// <summary>Mouse position where a potential drag started (List).</summary>
    private Point _dragStart;
    private bool _maybeDrag;

    /// <summary>The row under the press that armed a potential drag.</summary>
    private FileItemRow? _dragRow;

    /// <summary>
    /// Set when a plain click landed on an already-selected row: the multi-selection
    /// is kept until mouse-up so the row can start a drag (Explorer deferred select).
    /// </summary>
    private bool _deferSelectionClear;

    /// <summary>The drag-count badge shown while dragging two or more items.</summary>
    private DragCountOverlay? _dragOverlay;

    private readonly IDragOverlayInterop _overlayInterop;

    public MainWindow(
        MainWindowViewModel viewModel,
        TabManager tabManager,
        AppState appState,
        FileOpsService fileOps,
        ISpecialFolderSource specialFolders,
        ShellMenuHost shellMenu,
        Actions.ActionDispatcher dispatcher,
        IDragOverlayInterop overlayInterop
    )
    {
        _tabManager = tabManager;
        _appState = appState;
        _fileOps = fileOps;
        _specialFolders = specialFolders;
        _shellMenu = shellMenu;
        _dispatcher = dispatcher;
        _overlayInterop = overlayInterop;
        _dispatcher.AttachView(new ViewEffects(this));
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
        if (e.ChangedButton == MouseButton.XButton1)
        {
            _dispatcher.Dispatch(new AppAction.GoBack());
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            _dispatcher.Dispatch(new AppAction.GoForward());
            e.Handled = true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
    }

    // Keyboard dispatch

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // Focused text inputs (address/command bars, inline rename) type freely.
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        TabState tabState = _tabManager.GetActiveTabState();

        // Alt+arrow arrives as a system key, so read SystemKey in that case.
        Key physical = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!WpfKeyTranslator.TryTranslate(physical, out AppKey appKey))
        {
            return;
        }
        AppModifiers mods = WpfKeyTranslator.Modifiers(e.KeyboardDevice.Modifiers);

        // Two-key chords (yy / dd) apply only in NORMAL with list focus, no modifier.
        bool chordEligible =
            tabState.ModeValue is Mode.Normal
            && tabState.FocusValue == VFocus.List
            && mods == AppModifiers.None;
        KeyChord? chord = _chordResolver.Resolve(appKey, mods, chordEligible);
        if (chord is null)
        {
            // First key of a pending chord — wait for the second.
            e.Handled = true;
            return;
        }

        if (KeyBindingMap.Default.Resolve(BuildKeyContext(tabState), chord.Value) is { } action)
        {
            _dispatcher.Dispatch(action);
            e.Handled = true;
        }
    }

    private static KeyContext BuildKeyContext(TabState tabState)
    {
        return new(
            tabState.ModeValue.Kind(),
            tabState.FocusValue,
            KnownLocations.IsRecycleBin(tabState.CurrentLocationValue)
        );
    }

    // Inline rename

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

    // Context menu (MENU mode)

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
        HostedMenuSession? session;
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
        bool inBin = KnownLocations.IsRecycleBin(tab.CurrentLocationValue);
        return
        [
            new() { Text = "Open", SelfAction = () => Dispatch(new AppAction.ActivateItem()) },
            new() { Text = "Open With", SelfAction = () => Dispatch(new AppAction.OpenWith("")) },
            MenuItemViewModel.Separator,
            new() { Text = "Cut", SelfAction = () => Dispatch(new AppAction.Yank(Cut: true)) },
            new() { Text = "Copy", SelfAction = () => Dispatch(new AppAction.Yank(Cut: false)) },
            new() { Text = "Paste", SelfAction = () => Dispatch(new AppAction.Paste()) },
            new() { Text = "Copy Path", SelfAction = () => Dispatch(new AppAction.CopyPaths()) },
            MenuItemViewModel.Separator,
            new() { Text = "Rename", SelfAction = () => Dispatch(new AppAction.BeginRename()) },
            new()
            {
                Text = "Trash",
                // Inside the bin, "Trash" is a permanent delete (matches the x key).
                SelfAction = () =>
                    Dispatch(inBin ? new AppAction.DeletePermanent() : new AppAction.Trash()),
            },
            new() { Text = "Delete", SelfAction = () => Dispatch(new AppAction.DeletePermanent()) },
            MenuItemViewModel.Separator,
            new() { Text = "Zip", SelfAction = () => Dispatch(new AppAction.Zip("")) },
            new() { Text = "Unzip", SelfAction = () => Dispatch(new AppAction.Unzip("")) },
            new()
            {
                Text = "Properties",
                SelfAction = () => Dispatch(new AppAction.ShowProperties("")),
            },
            MenuItemViewModel.Separator,
            new()
            {
                Text = "New File",
                SelfAction = () =>
                    Dispatch(new AppAction.EnterMode(ModeTarget.Command, "newfile ")),
            },
            new()
            {
                Text = "New Folder",
                SelfAction = () => Dispatch(new AppAction.EnterMode(ModeTarget.Command, "mkdir ")),
            },
            MenuItemViewModel.Separator,
            new()
            {
                Text = "Pin",
                SelfSubItems =
                [
                    new()
                    {
                        Text = "Pin to Programs",
                        SelfAction = () => Dispatch(new AppAction.Pin("programs")),
                    },
                    new()
                    {
                        Text = "Pin to Desktop",
                        SelfAction = () => Dispatch(new AppAction.Pin("desktop")),
                    },
                ],
            },
            MenuItemViewModel.Separator,
            new() { Text = "Undo", SelfAction = () => Dispatch(new AppAction.Undo()) },
            new() { Text = "Redo", SelfAction = () => Dispatch(new AppAction.Redo()) },
        ];
    }

    private List<MenuItemViewModel> BuildBackgroundMenuItems(MainWindowViewModel vm, TabState tab)
    {
        return
        [
            new() { Text = "Paste", SelfAction = () => Dispatch(new AppAction.Paste()) },
            new()
            {
                Text = "New File",
                SelfAction = () =>
                    Dispatch(new AppAction.EnterMode(ModeTarget.Command, "newfile ")),
            },
            new()
            {
                Text = "New Folder",
                SelfAction = () => Dispatch(new AppAction.EnterMode(ModeTarget.Command, "mkdir ")),
            },
            new() { Text = "Copy Path", SelfAction = () => CopyCurrentFolderPath(tab) },
            new()
            {
                Text = "Open Terminal",
                SelfAction = () => Dispatch(new AppAction.OpenTerminal("")),
            },
            MenuItemViewModel.Separator,
            new() { Text = "Undo", SelfAction = () => Dispatch(new AppAction.Undo()) },
            new() { Text = "Redo", SelfAction = () => Dispatch(new AppAction.Redo()) },
        ];
    }

    /// <summary>Dispatches a menu-originated action through the central dispatcher.</summary>
    private void Dispatch(AppAction action)
    {
        _dispatcher.Dispatch(action);
    }

    private void CopyCurrentFolderPath(TabState tab)
    {
        if (tab.CurrentDirectoryPath is string dir)
        {
            ShellClipboard.SetText(dir);
            tab.SetStatusMessage("Copied folder path", isError: false);
        }
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
    /// Routes character input (":", "!", "/", "[ ] { }") through the binding table.
    /// Using text input (not physical keys) keeps these layout-independent — on JIS
    /// ":" is a dedicated key, not Shift+";".
    /// </summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);

        // Text inputs (bars, inline rename) handle their own typing.
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        TabState tabState = _tabManager.GetActiveTabState();
        if (KeyBindingMap.Default.ResolveText(BuildKeyContext(tabState), e.Text) is { } action)
        {
            _dispatcher.Dispatch(action);
            e.Handled = true;
        }
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

    // Address-bar toolbar buttons (delegate to the same paths as the keys)

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

    // Mouse handlers

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

        _deferSelectionClear = false;
        if (ctrl && row is { IsParentEntry: false })
        {
            tabState.SetSelection(vm.FileList.ToggleSelection(tabState.SelectionValue, row.Index));
        }
        else if (shift && row is { IsParentEntry: false })
        {
            tabState.SetSelection(vm.FileList.RangeSelection(vm.FileList.CursorIndex, row.Index));
        }
        else if (row is { IsParentEntry: false } && tabState.SelectionValue.Contains(row.Index))
        {
            // Plain click on an already-selected row: keep the multi-selection so a
            // drag can carry it, and clear it on mouse-up only if no drag happened.
            _deferSelectionClear = true;
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

    /// <summary>
    /// Completes a deferred selection clear: if a plain click on an already-selected
    /// row did not turn into a drag, collapse the multi-selection on release.
    /// </summary>
    private void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferSelectionClear)
        {
            _deferSelectionClear = false;
            _tabManager
                .GetActiveTabState()
                .SetSelection(System.Collections.Immutable.ImmutableHashSet<int>.Empty);
        }
    }

    // Drag & drop

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

        // A drag is starting, so the deferred mouse-up clear must not fire.
        _deferSelectionClear = false;

        DataObject data = ShellClipboard.BuildDataObject(paths);
        _dragOverlay =
            paths.Count >= 2 ? DragCountOverlay.Begin(paths.Count, _overlayInterop) : null;
        try
        {
            DragDrop.DoDragDrop(FileList, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            _dragOverlay?.Close();
            _dragOverlay = null;
        }
    }

    /// <summary>Trails the drag-count badge under the cursor while a multi-item drag is active.</summary>
    private void FileList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_dragOverlay is not null && _overlayInterop.TryGetCursorPosition(out int x, out int y))
        {
            _dragOverlay.MoveToScreen(x, y);
        }
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
            d = GetParent(d);
        }
        return null;
    }

    /// <summary>Walks up the tree from a click source to the bound FileItemRow.</summary>
    private static FileItemRow? FindRow(object source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement { DataContext: FileItemRow row })
            {
                return row;
            }
            d = GetParent(d);
        }
        return null;
    }

    /// <summary>
    /// Parent walk that tolerates content elements. <c>e.OriginalSource</c> can be a
    /// <see cref="System.Windows.Documents.Run"/> (HighlightBehavior fills row labels with runs),
    /// which is not a Visual — <see cref="VisualTreeHelper.GetParent"/> would throw on it, so fall
    /// back to the logical tree for non-Visual nodes.
    /// </summary>
    private static DependencyObject? GetParent(DependencyObject d)
    {
        if (d is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(d);
        }
        return LogicalTreeHelper.GetParent(d);
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
        int idx = FileList.SelectedIndex;
        if (idx < 0)
        {
            return;
        }
        vm.FileList.CursorIndex = idx;
        _dispatcher.Dispatch(new AppAction.ActivateItem());
    }

    // Helpers

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

    // Column sort

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
