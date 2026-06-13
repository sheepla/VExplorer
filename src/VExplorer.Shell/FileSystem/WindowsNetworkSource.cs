using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Enumerates the Network shell folder via the late-bound <c>Shell.Application</c>
/// automation object (same approach as the Recycle Bin). Failures and timeouts
/// yield an empty list rather than throwing.
/// </summary>
public sealed class WindowsNetworkSource : INetworkSource
{
    // ssfNETWORK — the Network root for Shell.NameSpace.
    private const int SsfNetwork = 0x12;

    public IReadOnlyList<NetworkEntry> List()
    {
        List<NetworkEntry> result = [];
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
            dynamic net = shell.NameSpace(SsfNetwork);
            if (net == null)
            {
                return result;
            }
            foreach (dynamic item in net.Items())
            {
                try
                {
                    string name = (string)item.Name ?? "";
                    string path = (string)item.Path ?? "";
                    bool isFolder = (bool)item.IsFolder;
                    if (name.Length > 0)
                    {
                        result.Add(new NetworkEntry(name, path, isFolder));
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
