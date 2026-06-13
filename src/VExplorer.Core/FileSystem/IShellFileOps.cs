namespace VExplorer.Core.FileSystem;

/// <summary>Outcome of a shell file operation.</summary>
public enum ShellOpStatus
{
    Success,

    /// <summary>The user cancelled the OS dialog (no error to report).</summary>
    Cancelled,

    Error,
}

/// <summary>Result of a shell file operation, carrying an error message when failed.</summary>
public readonly record struct ShellOpResult(ShellOpStatus Status, string? ErrorMessage)
{
    public bool IsSuccess => Status == ShellOpStatus.Success;

    public static ShellOpResult Ok()
    {
        return new(ShellOpStatus.Success, null);
    }

    public static ShellOpResult Cancelled()
    {
        return new(ShellOpStatus.Cancelled, null);
    }

    public static ShellOpResult Fail(string message)
    {
        return new(ShellOpStatus.Error, message);
    }
}

/// <summary>
/// File operations delegated to the Windows shell (<c>IFileOperation</c>), so the
/// OS provides the progress dialog, overwrite/conflict prompts and delete
/// confirmation. The owner window handle (<paramref name="ownerHwnd"/>) parents
/// those dialogs; pass <c>0</c> for none.
/// </summary>
public interface IShellFileOps
{
    /// <summary>
    /// Copy into <paramref name="destinationDirectory"/>. When
    /// <paramref name="autoRename"/> is true, name collisions are auto-renamed
    /// ("file (2)") instead of prompting — used for in-place duplication.
    /// </summary>
    Task<ShellOpResult> CopyAsync(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        nint ownerHwnd,
        bool autoRename = false,
        CancellationToken cancel = default
    );

    Task<ShellOpResult> MoveAsync(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        nint ownerHwnd,
        bool autoRename = false,
        CancellationToken cancel = default
    );

    /// <summary>Delete to the Recycle Bin (<paramref name="recycle"/>=true) or permanently.</summary>
    Task<ShellOpResult> DeleteAsync(
        IReadOnlyList<string> sources,
        bool recycle,
        nint ownerHwnd,
        CancellationToken cancel = default
    );

    Task<ShellOpResult> RenameAsync(
        string source,
        string newName,
        nint ownerHwnd,
        CancellationToken cancel = default
    );

    /// <summary>
    /// Copy <paramref name="source"/> into the existing
    /// <paramref name="destinationDirectory"/> under <paramref name="newName"/>
    /// (Unix <c>cp SRC DEST</c> where DEST names the target). OS shows overwrite prompts.
    /// </summary>
    Task<ShellOpResult> CopyRenameAsync(
        string source,
        string destinationDirectory,
        string newName,
        nint ownerHwnd,
        CancellationToken cancel = default
    );

    /// <summary>Move variant of <see cref="CopyRenameAsync"/> (Unix <c>mv SRC DEST</c>).</summary>
    Task<ShellOpResult> MoveRenameAsync(
        string source,
        string destinationDirectory,
        string newName,
        nint ownerHwnd,
        CancellationToken cancel = default
    );

    Task<ShellOpResult> NewFolderAsync(
        string destinationDirectory,
        string folderName,
        nint ownerHwnd,
        CancellationToken cancel = default
    );

    /// <summary>
    /// Create an empty file named <paramref name="fileName"/> in
    /// <paramref name="destinationDirectory"/> (which must already exist).
    /// </summary>
    Task<ShellOpResult> NewFileAsync(
        string destinationDirectory,
        string fileName,
        nint ownerHwnd,
        CancellationToken cancel = default
    );
}
