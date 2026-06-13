using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VExplorer.App.Features.Shell;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.FileList;

/// <summary>
/// Resolves a single row's shell type name and icon on a worker thread and applies
/// them to the row on the UI thread. With the list virtualized, only the rows the
/// view actually realizes are enriched — so opening a folder no longer fans out a
/// shell call (and a dispatcher post) for every entry, which was the main freeze on
/// huge folders. A per-listing <see cref="CancellationToken"/> drops stale work when
/// the user navigates away before the icons arrive.
/// </summary>
internal sealed class RowEnricher(IShellInfoProvider shellInfo, IIconImageCache iconCache)
{
    private readonly IShellInfoProvider _shellInfo = shellInfo;
    private readonly IIconImageCache _iconCache = iconCache;

    // Captured on the UI thread (the VM is constructed there) so enrichment results
    // can be marshalled back regardless of which worker thread resolved them.
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public void Enrich(FileItemRow row, CancellationToken token)
    {
        if (row.IsParentEntry || token.IsCancellationRequested)
        {
            return;
        }

        Location loc = row.Location;
        string extension = row.Extension;
        bool isDirectory = row.IsDirectory;

        Task.Run(
            () =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                ShellItemInfo info = _shellInfo.Resolve(loc, extension, isDirectory);
                BitmapSource? icon = _iconCache.GetIcon(
                    info.SystemIconIndex,
                    () => _shellInfo.CopyIcon(loc, extension, isDirectory),
                    isDirectory
                );

                _dispatcher.BeginInvoke(
                    () =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        if (!string.IsNullOrEmpty(info.TypeName))
                        {
                            row.TypeDisplay = info.TypeName;
                        }
                        row.Icon = icon;
                    },
                    DispatcherPriority.Background
                );
            },
            token
        );
    }
}
