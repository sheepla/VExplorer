namespace VExplorer.Core.FileSystem;

public interface IDirectoryLister
{
    /// <summary>
    /// Lists the contents of <paramref name="location"/>. Filesystem locations are
    /// enumerated directly; shell-namespace locations (PC root, Recycle Bin, …) are
    /// resolved by the implementation. <paramref name="options"/> can pre-filter the
    /// listing and bound its time (see <see cref="ListOptions"/>); pass
    /// <c>default</c> for the unfiltered, unbounded listing.
    /// </summary>
    ValueTask<IFileItemSource> ListAsync(
        Location location,
        ListOptions options,
        CancellationToken cancel
    );
}
