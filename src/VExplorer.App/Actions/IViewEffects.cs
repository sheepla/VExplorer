namespace VExplorer.App.Actions;

/// <summary>
/// View-side effects the dispatcher needs but cannot perform itself: scrolling,
/// moving keyboard focus, inline rename, and driving the context-menu popup.
/// Implemented by the window and attached to the dispatcher, keeping action
/// handlers free of direct WPF control access.
/// </summary>
public interface IViewEffects
{
    nint OwnerHwnd { get; }
    int PageSize { get; }
    int HalfPageSize { get; }

    void ScrollListToCursor();
    void ScrollTreeToCursor();

    void FocusAddressBar();
    void FocusCommandBar(string? seed);
    void EnterSearch();
    void EnterFilter();
    void BeginInlineRename();
    void NavigateListToTreeSelection();

    void OpenContextMenuAtCursor();
    void MenuMoveDown();
    void MenuMoveUp();
    void MenuMoveToFirst();
    void MenuMoveToLast();
    void MenuPageDown();
    void MenuPageUp();
    void MenuEnterSubmenu();
    void MenuBack();
    void MenuInvoke();
    void MenuClose();
}
