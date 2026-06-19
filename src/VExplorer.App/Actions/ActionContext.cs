using VExplorer.App.Features.FileList;
using VExplorer.App.Features.Tree;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Actions;

/// <summary>
/// The active tab's state and view models snapshotted for one dispatch, plus the
/// page sizes and owner window needed by handlers. <see cref="CursorTarget"/>
/// resolves the focused pane so focus-dependent actions don't branch at each site.
/// </summary>
public sealed record ActionContext(
    TabState Tab,
    FileListViewModel List,
    TreeViewModel Tree,
    Focus Focus,
    nint OwnerHwnd,
    int PageSize,
    int HalfPageSize
)
{
    public ICursorTarget CursorTarget => Focus == Focus.List ? List : Tree;
}
