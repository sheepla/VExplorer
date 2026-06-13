using System.Windows.Media.Imaging;

namespace VExplorer.App.Features.Shell;

/// <summary>
/// Caches frozen <see cref="BitmapSource"/> icons keyed by the system image-list
/// index, so two file types that share an icon share one bitmap. Thread-safe:
/// returned bitmaps are frozen and may be produced on a worker thread and applied
/// on the UI thread.
/// </summary>
public interface IIconImageCache
{
    /// <summary>
    /// Returns the cached icon for <paramref name="iconIndex"/>, materializing it
    /// via <paramref name="hIconFactory"/> on a miss. The factory returns a fresh
    /// <c>HICON</c> which the cache converts and then destroys. When resolution
    /// yields nothing (e.g. a shell item with no obtainable icon), falls back to
    /// the generic folder or file icon per <paramref name="isDirectory"/>.
    /// </summary>
    BitmapSource? GetIcon(int iconIndex, Func<nint> hIconFactory, bool isDirectory);
}
