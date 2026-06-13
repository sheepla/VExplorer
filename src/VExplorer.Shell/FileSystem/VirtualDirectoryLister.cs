using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// <see cref="IDirectoryLister"/> that dispatches by <see cref="Location"/>: the PC
/// root and virtual destinations (Recycle Bin, Network) are handled via
/// <see cref="ILocationService"/>; special "place" folders are resolved to their
/// physical path and, like every filesystem location, delegated to the inner
/// physical lister. See <c>VExplorer_SpecialFolders.md</c>.
/// </summary>
public sealed class VirtualDirectoryLister(
    IDirectoryLister physical,
    ILocationService locations,
    IRecycleBinSource recycleBin,
    INetworkSource network
) : IDirectoryLister
{
    private readonly IDirectoryLister _physical = physical;
    private readonly ILocationService _locations = locations;
    private readonly IRecycleBinSource _recycleBin = recycleBin;
    private readonly INetworkSource _network = network;

    public ValueTask<IFileItemSource> ListAsync(
        Location location,
        ListOptions options,
        CancellationToken cancel
    )
    {
        // Shell-namespace listings (PC root, Recycle Bin, Network) are small and
        // synthesized in full, so the filter/timeout options do not apply to them.
        if (KnownLocations.IsPc(location))
        {
            return new ValueTask<IFileItemSource>(_locations.ListPcRoot());
        }
        if (KnownLocations.IsRecycleBin(location))
        {
            return new ValueTask<IFileItemSource>(
                Task.Run<IFileItemSource>(ListRecycleBin, cancel)
            );
        }
        if (KnownLocations.IsNetwork(location))
        {
            return new ValueTask<IFileItemSource>(Task.Run<IFileItemSource>(ListNetwork, cancel));
        }
        if (_locations.TryGetListingPath(location, out string physicalPath))
        {
            return _physical.ListAsync(Location.ForPath(physicalPath), options, cancel);
        }
        return new ValueTask<IFileItemSource>(InMemoryFileItemSource.Empty);
    }

    private IFileItemSource ListRecycleBin()
    {
        List<FileItem> items = [];
        foreach (RecycleBinItem item in _recycleBin.List())
        {
            items.Add(
                new FileItem
                {
                    Name = item.DisplayName,
                    // Original path drives the type-icon (by extension) and is shown
                    // as the item's location; navigation into bin rows is suppressed.
                    FullPath = item.OriginalPath,
                    IsDirectory = item.IsDirectory,
                    SizeBytes = item.SizeBytes,
                    LastWriteTimeUtc = item.DeletedUtc ?? default,
                    Extension = item.IsDirectory ? "" : Path.GetExtension(item.DisplayName),
                    Kind = FileItemKind.Virtual,
                    ShellToken = item.Token,
                }
            );
        }
        return new InMemoryFileItemSource(items);
    }

    private IFileItemSource ListNetwork()
    {
        List<FileItem> items = [];
        foreach (NetworkEntry entry in _network.List())
        {
            items.Add(
                new FileItem
                {
                    Name = entry.DisplayName,
                    FullPath = entry.UncPath,
                    IsDirectory = entry.IsContainer,
                }
            );
        }
        return new InMemoryFileItemSource(items);
    }
}
