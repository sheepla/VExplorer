using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Enumerates a server's shares via the late-bound <c>Shell.Application</c>
/// automation object — the same approach as <see cref="WindowsNetworkSource"/>.
/// This surfaces ordinary (visible) shares; hidden administrative shares
/// (<c>C$</c> etc.) are intentionally not listed. Failures and timeouts yield an
/// empty list rather than throwing.
/// </summary>
public sealed class WindowsUncShareSource : IUncShareSource
{
    public IReadOnlyList<UncShareEntry> ListShares(string server)
    {
        List<UncShareEntry> result = [];
        if (string.IsNullOrWhiteSpace(server))
        {
            return result;
        }

        Type? t = Type.GetTypeFromProgID("Shell.Application");
        if (t == null)
        {
            return result;
        }

        dynamic? shell = Activator.CreateInstance(t);
        if (shell == null)
        {
            return result;
        }
        try
        {
            dynamic? folder = shell.NameSpace($@"\\{server}");
            if (folder == null)
            {
                return result;
            }
            foreach (dynamic item in folder.Items())
            {
                try
                {
                    if (!(bool)item.IsFolder)
                    {
                        continue;
                    }
                    string name = (string)item.Name ?? "";
                    string path = (string)item.Path ?? "";
                    if (name.Length > 0 && path.Length > 0)
                    {
                        result.Add(new UncShareEntry(name, path));
                    }
                }
                catch
                {
                    // Skip entries that fail to read.
                }
            }
        }
        catch
        {
            // Offline / timeout / access errors → empty list.
        }
        finally
        {
            if (Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
        return result;
    }
}
