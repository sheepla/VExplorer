using System.Collections;

namespace VExplorer.Core.FileSystem;

public sealed class InMemoryFileItemSource(IReadOnlyList<FileItem> items, bool isTruncated = false)
    : IFileItemSource,
        IReadOnlyList<FileItem>
{
    private readonly IReadOnlyList<FileItem> _items = items;

    public static readonly InMemoryFileItemSource Empty = new(Array.Empty<FileItem>());

    public int Count => _items.Count;

    /// <summary>True when a time budget cut the listing short (see <see cref="ListOptions"/>).</summary>
    public bool IsTruncated { get; } = isTruncated;

    public FileItem this[int index] => _items[index];

    public IEnumerator<FileItem> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    System.Collections.IEnumerator IEnumerable.GetEnumerator()
    {
        return ((System.Collections.IEnumerable)_items).GetEnumerator();
    }
}
