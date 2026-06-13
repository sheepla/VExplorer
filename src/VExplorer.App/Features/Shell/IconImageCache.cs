using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.Shell;

/// <summary>
/// <see cref="IIconImageCache"/> backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// The single place that turns an <c>HICON</c> into a <see cref="BitmapSource"/>;
/// the handle is always destroyed in a <c>finally</c> so icons never leak.
/// </summary>
public sealed class IconImageCache(IShellInfoProvider provider) : IIconImageCache
{
    private readonly IShellInfoProvider _provider = provider;
    private readonly ConcurrentDictionary<int, BitmapSource?> _cache = new();
    private readonly object _fallbackLock = new();
    private BitmapSource? _folderFallback;
    private BitmapSource? _fileFallback;
    private bool _folderFallbackBuilt;
    private bool _fileFallbackBuilt;

    public BitmapSource? GetIcon(int iconIndex, Func<nint> hIconFactory, bool isDirectory)
    {
        if (_cache.TryGetValue(iconIndex, out BitmapSource? cached) && cached is not null)
        {
            return cached;
        }

        BitmapSource? icon = Materialize(hIconFactory);
        if (icon is not null)
        {
            // Only cache successes: a null must not poison this index for another
            // item that legitimately resolves to the same system image-list slot.
            _cache[iconIndex] = icon;
            return icon;
        }

        // No obtainable icon (e.g. a shell item whose provider yields none):
        // fall back to the generic folder / file icon so the row is never blank.
        return Fallback(isDirectory);
    }

    /// <summary>The generic folder or file icon, built once on demand.</summary>
    private BitmapSource? Fallback(bool isDirectory)
    {
        lock (_fallbackLock)
        {
            if (isDirectory)
            {
                if (!_folderFallbackBuilt)
                {
                    _folderFallback = Materialize(() => _provider.CopyIcon("dir", "", true));
                    _folderFallbackBuilt = true;
                }
                return _folderFallback;
            }
            if (!_fileFallbackBuilt)
            {
                _fileFallback = Materialize(() => _provider.CopyIcon("file", "", false));
                _fileFallbackBuilt = true;
            }
            return _fileFallback;
        }
    }

    private BitmapSource? Materialize(Func<nint> hIconFactory)
    {
        nint hIcon = hIconFactory();
        if (hIcon == 0)
        {
            return null;
        }

        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
            source.Freeze(); // shareable across threads
            return source;
        }
        finally
        {
            _provider.DestroyIcon(hIcon);
        }
    }
}
