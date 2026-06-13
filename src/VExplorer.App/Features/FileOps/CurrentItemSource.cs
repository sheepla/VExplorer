using VExplorer.App.Features.FileList;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.FileOps;

/// <summary>
/// Supplies the active tab's cursor item name for <c>:rename</c> completion.
/// </summary>
public sealed class CurrentItemSource(TabManager tabManager) : ICurrentItemSource
{
    private readonly TabManager _tabManager = tabManager;

    public string? GetCurrentItemName()
    {
        FileItemRow? row = _tabManager.GetActiveScopedService<FileListViewModel>().CursorRow;
        return row is { IsParentEntry: false } ? row.Name : null;
    }
}
