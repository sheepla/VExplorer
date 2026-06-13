namespace VExplorer.Core.FileSystem;

public interface IFileItemSource
{
    int Count { get; }
    FileItem this[int index] { get; }

    /// <summary>
    /// True when the listing was cut short by a time budget and does not contain
    /// every entry (see <see cref="ListOptions.TimeoutMs"/>). The view surfaces this
    /// so the user can choose to load the rest.
    /// </summary>
    bool IsTruncated { get; }
}
