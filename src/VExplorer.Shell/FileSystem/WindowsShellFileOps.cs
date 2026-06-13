using System.Diagnostics;
using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Implements <see cref="IShellFileOps"/> with the Windows <c>IFileOperation</c>
/// COM API. The OS provides the progress dialog, conflict/overwrite prompts and
/// delete confirmation.
/// <para>
/// <c>IFileOperation</c> requires an STA thread and <c>PerformOperations</c>
/// shows modal UI that pumps messages, so operations run synchronously on the
/// calling (UI) thread — a deliberate departure from the <c>Task.Run</c> pattern
/// used by the listers. The async signatures are kept for call-site uniformity.
/// </para>
/// </summary>
public sealed class WindowsShellFileOps : IShellFileOps
{
    // CLSID_FileOperation. Used with CoCreateInstance (coclass activation via
    // CsWin32 is not relied upon).
    private static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");

    public Task<ShellOpResult> CopyAsync(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        nint ownerHwnd,
        bool autoRename = false,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                WithRename(FILEOPERATION_FLAGS.FOF_ALLOWUNDO, autoRename),
                ownerHwnd,
                op =>
                {
                    IShellItem dest = CreateItem(destinationDirectory);
                    foreach (string src in sources)
                    {
                        op.CopyItem(CreateItem(src), dest, null, null);
                    }
                }
            )
        );
    }

    public Task<ShellOpResult> MoveAsync(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        nint ownerHwnd,
        bool autoRename = false,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                WithRename(FILEOPERATION_FLAGS.FOF_ALLOWUNDO, autoRename),
                ownerHwnd,
                op =>
                {
                    IShellItem dest = CreateItem(destinationDirectory);
                    foreach (string src in sources)
                    {
                        op.MoveItem(CreateItem(src), dest, null, null);
                    }
                }
            )
        );
    }

    public Task<ShellOpResult> CopyRenameAsync(
        string source,
        string destinationDirectory,
        string newName,
        nint ownerHwnd,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                FILEOPERATION_FLAGS.FOF_ALLOWUNDO,
                ownerHwnd,
                op =>
                    op.CopyItem(CreateItem(source), CreateItem(destinationDirectory), newName, null)
            )
        );
    }

    public Task<ShellOpResult> MoveRenameAsync(
        string source,
        string destinationDirectory,
        string newName,
        nint ownerHwnd,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                FILEOPERATION_FLAGS.FOF_ALLOWUNDO,
                ownerHwnd,
                op =>
                    op.MoveItem(CreateItem(source), CreateItem(destinationDirectory), newName, null)
            )
        );
    }

    private static FILEOPERATION_FLAGS WithRename(FILEOPERATION_FLAGS flags, bool autoRename)
    {
        return autoRename ? flags | FILEOPERATION_FLAGS.FOF_RENAMEONCOLLISION : flags;
    }

    public Task<ShellOpResult> DeleteAsync(
        IReadOnlyList<string> sources,
        bool recycle,
        nint ownerHwnd,
        CancellationToken cancel = default
    )
    {
        FILEOPERATION_FLAGS flags = recycle
            ? FILEOPERATION_FLAGS.FOF_ALLOWUNDO | FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE
            : 0;
        return Task.FromResult(
            Run(
                flags,
                ownerHwnd,
                op =>
                {
                    foreach (string src in sources)
                    {
                        op.DeleteItem(CreateItem(src), null);
                    }
                }
            )
        );
    }

    public Task<ShellOpResult> RenameAsync(
        string source,
        string newName,
        nint ownerHwnd,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                FILEOPERATION_FLAGS.FOF_ALLOWUNDO,
                ownerHwnd,
                op => op.RenameItem(CreateItem(source), newName, null)
            )
        );
    }

    public Task<ShellOpResult> NewFolderAsync(
        string destinationDirectory,
        string folderName,
        nint ownerHwnd,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                0,
                ownerHwnd,
                op =>
                    op.NewItem(
                        CreateItem(destinationDirectory),
                        (uint)FileAttributes.Directory,
                        folderName,
                        null,
                        null
                    )
            )
        );
    }

    public Task<ShellOpResult> NewFileAsync(
        string destinationDirectory,
        string fileName,
        nint ownerHwnd,
        CancellationToken cancel = default
    )
    {
        return Task.FromResult(
            Run(
                0,
                ownerHwnd,
                op =>
                    op.NewItem(
                        CreateItem(destinationDirectory),
                        (uint)FileAttributes.Normal,
                        fileName,
                        null,
                        null
                    )
            )
        );
    }

    private static ShellOpResult Run(
        FILEOPERATION_FLAGS flags,
        nint ownerHwnd,
        Action<IFileOperation> queue
    )
    {
        Debug.Assert(
            Thread.CurrentThread.GetApartmentState() == ApartmentState.STA,
            "IFileOperation must run on an STA thread."
        );

        IFileOperation? op = null;
        try
        {
            PInvoke.CoCreateInstance(
                CLSID_FileOperation,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                out op
            );

            op.SetOperationFlags(flags);
            if (ownerHwnd != 0)
            {
                op.SetOwnerWindow(new HWND(ownerHwnd));
            }

            queue(op);
            op.PerformOperations();

            op.GetAnyOperationsAborted(out BOOL aborted);
            return aborted ? ShellOpResult.Cancelled() : ShellOpResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return ShellOpResult.Cancelled();
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x800704C7)
        {
            // ERROR_CANCELLED — user dismissed a prompt (e.g. UAC).
            return ShellOpResult.Cancelled();
        }
        catch (Exception ex)
        {
            // Never let a shell failure crash the app — surface readable text.
            return ShellOpResult.Fail(Humanize(ex));
        }
        finally
        {
            if (op != null)
            {
                Marshal.ReleaseComObject(op);
            }
        }
    }

    private static IShellItem CreateItem(string path)
    {
        HRESULT hr = PInvoke.SHCreateItemFromParsingName(
            path,
            null,
            typeof(IShellItem).GUID,
            out object item
        );
        if (hr.Failed || item is not IShellItem shellItem)
        {
            // Throw so Run() converts it to a readable ShellOpResult.Fail.
            Marshal.ThrowExceptionForHR(hr.Value);
            throw new InvalidOperationException($"Cannot resolve path: {path}");
        }
        return shellItem;
    }

    /// <summary>Converts an exception / HRESULT to a human-readable message.</summary>
    private static string Humanize(Exception ex)
    {
        // COM/Win32 messages are already localized text; prefer the HRESULT's
        // canonical message when the raw message is empty or a bare code.
        Exception? mapped = Marshal.GetExceptionForHR(ex.HResult);
        string message = !string.IsNullOrWhiteSpace(ex.Message)
            ? ex.Message
            : mapped?.Message ?? "";
        return string.IsNullOrWhiteSpace(message)
            ? $"Operation failed (0x{(uint)ex.HResult:X8})"
            : message;
    }
}
