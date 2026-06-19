using System.Windows.Threading;
using VExplorer.App.Features.Menu;
using VExplorer.Core.Modes;

namespace VExplorer.App;

public partial class MainWindow
{
    /// <summary>
    /// Adapts the window's view operations to <see cref="Actions.IViewEffects"/> so
    /// the dispatcher can drive scrolling, focus, inline rename and the context
    /// menu without taking a direct dependency on the window.
    /// </summary>
    private sealed class ViewEffects(MainWindow w) : Actions.IViewEffects
    {
        public nint OwnerHwnd => w._hwnd;
        public int PageSize => w.GetPageSize();
        public int HalfPageSize => w.GetHalfPageSize();

        public void ScrollListToCursor()
        {
            w.ScrollFileListToCursor();
        }

        public void ScrollTreeToCursor()
        {
            w.ScrollTreeToCursor();
        }

        // Focus after the mode change makes the input box visible (its IsActive
        // binding applies on the next layout pass), so defer to Input priority.
        public void FocusAddressBar()
        {
            w.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => w.AddressBarControl.FocusInput())
            );
        }

        public void FocusCommandBar(string? seed)
        {
            w.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    w.CommandBarControl.FocusInput();
                    if (seed is { Length: > 0 })
                    {
                        w.CommandBarControl.Prefill(seed);
                    }
                })
            );
        }

        public void EnterSearch()
        {
            w.EnterSearchOrFilter(w._tabManager.GetActiveTabState(), new ModeEvent.EnterSearch());
        }

        public void EnterFilter()
        {
            w.EnterSearchOrFilter(w._tabManager.GetActiveTabState(), new ModeEvent.EnterFilter());
        }

        public void BeginInlineRename()
        {
            w.BeginInlineRename(Vm);
        }

        public void NavigateListToTreeSelection()
        {
            w.NavigateFileListToTreeSelection(w._tabManager.GetActiveTabState());
        }

        public void OpenContextMenuAtCursor()
        {
            w.OpenContextMenuAtCursor(Vm, w._tabManager.GetActiveTabState());
        }

        public void MenuMoveDown()
        {
            Menu.MoveDown();
        }

        public void MenuMoveUp()
        {
            Menu.MoveUp();
        }

        public void MenuMoveToFirst()
        {
            Menu.MoveToFirst();
        }

        public void MenuMoveToLast()
        {
            Menu.MoveToLast();
        }

        public void MenuPageDown()
        {
            Menu.PageDown();
        }

        public void MenuPageUp()
        {
            Menu.PageUp();
        }

        public void MenuEnterSubmenu()
        {
            _ = Menu.EnterSubmenuAsync();
        }

        public void MenuBack()
        {
            Menu.Back();
        }

        public void MenuInvoke()
        {
            w.InvokeMenuSelection(Vm, w._tabManager.GetActiveTabState());
        }

        public void MenuClose()
        {
            w.CloseContextMenu(w._tabManager.GetActiveTabState());
        }

        private MainWindowViewModel Vm => (MainWindowViewModel)w.DataContext;

        private ContextMenuViewModel Menu => Vm.ContextMenu;
    }
}
