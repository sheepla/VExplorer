using System.Collections;

namespace VExplorer.App.Features.FileList;

/// <summary>
/// Read-only, index-addressable list of <see cref="FileItemRow"/> that creates each
/// row lazily on first access and caches it. Bound as the ListView's
/// <c>ItemsSource</c>: WPF UI virtualization only asks for the indices it realizes,
/// so the heavy <see cref="FileItemRow"/> objects (and their shell enrichment) are
/// built only for visible rows — never for all entries of a huge folder.
///
/// <para>
/// It implements the non-generic <see cref="IList"/> so WPF's <c>ListCollectionView</c>
/// addresses it by index instead of copying it into an internal array (which would
/// materialize every row and defeat the purpose). The factory owns row creation
/// (parent "..", selection state, enrichment); this type only maps index → row and
/// caches. <see cref="IndexOf"/>/<see cref="Contains"/> use the row's own
/// <c>Index</c> so the view's currency tracking never forces a full scan.
/// </para>
/// </summary>
internal sealed class VirtualizedRowList(int count, Func<int, FileItemRow> factory)
    : IReadOnlyList<FileItemRow>,
        IList
{
    private readonly int _count = count;
    private readonly Func<int, FileItemRow> _factory = factory;
    private readonly Dictionary<int, FileItemRow> _cache = [];

    public int Count => _count;

    /// <summary>The rows realized so far (used to push selection onto visible rows only).</summary>
    public IReadOnlyCollection<FileItemRow> RealizedRows => _cache.Values;

    public FileItemRow this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if (!_cache.TryGetValue(index, out FileItemRow? row))
            {
                row = _factory(index);
                _cache[index] = row;
            }
            return row;
        }
    }

    public IEnumerator<FileItemRow> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // IList (read-only view)

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    bool IList.IsFixedSize => true;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    int IList.Add(object? value)
    {
        throw new NotSupportedException();
    }

    void IList.Clear()
    {
        throw new NotSupportedException();
    }

    void IList.Insert(int index, object? value)
    {
        throw new NotSupportedException();
    }

    void IList.Remove(object? value)
    {
        throw new NotSupportedException();
    }

    void IList.RemoveAt(int index)
    {
        throw new NotSupportedException();
    }

    bool IList.Contains(object? value)
    {
        return value is FileItemRow row && IndexOf(row) >= 0;
    }

    // The view asks for the index of an item it already holds; the row knows its own
    // display index, so answer in O(1) without realizing the whole list.
    int IList.IndexOf(object? value)
    {
        return value is FileItemRow row ? IndexOf(row) : -1;
    }

    private int IndexOf(FileItemRow row)
    {
        int index = row.Index;
        return index >= 0 && index < _count && ReferenceEquals(this[index], row) ? index : -1;
    }

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < _count; i++)
        {
            array.SetValue(this[i], index + i);
        }
    }
}
