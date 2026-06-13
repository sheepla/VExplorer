using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VExplorer.Core.FileSystem;

namespace VExplorer.App.Features.FileList;

/// <summary>
/// Display-layer wrapper around <see cref="FileItem"/> that exposes formatted
/// strings for ListView binding, plus the special ".." parent-entry sentinel.
/// Mutable display state (multi-select highlight, inline-rename) is observable.
/// </summary>
public sealed partial class FileItemRow : ObservableObject
{
    private readonly FileItem _source;

    /// <summary>Position in the current DisplayItems list (assigned on sort).</summary>
    public int Index { get; set; }

    /// <summary>True when this row is part of the multi-selection (rendered highlighted).</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True while the name is being edited inline (F2).</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Editable name buffer used during inline rename.</summary>
    [ObservableProperty]
    private string _editName = "";

    /// <summary>
    /// File type label. Seeded with a cheap extension-based fallback so the row
    /// renders immediately, then replaced with the shell-resolved name by the
    /// list's background enrichment pass.
    /// </summary>
    [ObservableProperty]
    private string _typeDisplay;

    /// <summary>
    /// Small shell icon, populated asynchronously by background enrichment.
    /// Null until resolved (the Name cell simply shows no icon meanwhile).
    /// </summary>
    [ObservableProperty]
    private BitmapSource? _icon;

    private FileItemRow(FileItem source, bool isParentEntry)
    {
        _source = source;
        IsParentEntry = isParentEntry;
        _typeDisplay = FallbackTypeDisplay(source, isParentEntry);
    }

    public static FileItemRow CreateParentEntry(Location parent)
    {
        parent.TryGetFileSystemPath(out string path);
        return new(
            new FileItem
            {
                Name = "..",
                FullPath = path,
                IsDirectory = true,
                SizeBytes = null,
                LastWriteTimeUtc = default,
                Extension = "",
                Identity = parent,
            },
            isParentEntry: true
        );
    }

    public static FileItemRow FromFileItem(FileItem item)
    {
        return new(item, isParentEntry: false);
    }

    public bool IsParentEntry { get; }
    public string Name => _source.Name;

    /// <summary>
    /// The label shown in the Name column: a special-folder's localized
    /// <see cref="FileItem.DisplayName"/> when present, otherwise <see cref="Name"/>.
    /// </summary>
    public string DisplayLabel => _source.DisplayName ?? _source.Name;

    /// <summary>The row's role (drive / alias / virtual), used for icon selection.</summary>
    public FileItemKind Kind => _source.Kind;

    /// <summary>The row's navigable location (shell identity for special rows, else its path).</summary>
    public Location Location => _source.ResolveLocation();

    /// <summary>Shell-source token (e.g. Recycle Bin item id); null for ordinary rows.</summary>
    public int? ShellToken => _source.ShellToken;
    public string FullPath => _source.FullPath;
    public bool IsDirectory => _source.IsDirectory;

    /// <summary>
    /// True for hidden/system files, which render dimmed (grey text + faint icon),
    /// matching Explorer. Never dims the ".." parent entry.
    /// </summary>
    public bool IsDimmed => !IsParentEntry && (_source.IsHidden || _source.IsSystem);

    /// <summary>Extension (e.g. ".txt"); empty for directories. Used by enrichment.</summary>
    internal string Extension => _source.Extension;

    /// <summary>Sort key for the Size column. Directories sort before files (-1).</summary>
    internal long SortableSize => IsDirectory ? -1L : (_source.SizeBytes ?? 0L);

    /// <summary>Sort key for the Modified column.</summary>
    internal DateTime SortableDate => _source.LastWriteTimeUtc;

    /// <summary>
    /// Cheap extension-based type label used until the shell name arrives: blank
    /// for "..", "Folder" for directories, "{EXT} File" for files with an
    /// extension (e.g. "TXT File"), or "File" otherwise.
    /// </summary>
    private static string FallbackTypeDisplay(FileItem source, bool isParentEntry)
    {
        if (isParentEntry)
        {
            return "";
        }
        if (source.IsDirectory)
        {
            return "Folder";
        }
        string ext = source.Extension.TrimStart('.');
        return ext.Length > 0 ? $"{ext.ToUpperInvariant()} File" : "File";
    }

    /// <summary>Sort key for the Type column.</summary>
    internal string SortableType => TypeDisplay;

    /// <summary>Human-readable size.  Blank for directories and parent entry.</summary>
    public string SizeDisplay
    {
        get
        {
            if (IsParentEntry || IsDirectory || _source.SizeBytes is not long bytes)
            {
                return "";
            }

            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024L * 1024 => $"{bytes / 1024.0:0.0} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
            };
        }
    }

    /// <summary>Local time formatted as <c>yyyy-MM-dd HH:mm</c>.  Blank for parent entry.</summary>
    public string ModifiedDisplay
    {
        get
        {
            if (IsParentEntry || _source.LastWriteTimeUtc == default)
            {
                return "";
            }

            return _source.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
    }
}
