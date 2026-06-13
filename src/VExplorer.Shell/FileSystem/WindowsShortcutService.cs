using System.IO;
using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Creates <c>.lnk</c> shortcuts with <c>IShellLink</c> + <c>IPersistFile</c>
/// (CLSID_ShellLink), so the OS writes the shortcut format.
/// </summary>
public sealed class WindowsShortcutService : IShortcutService
{
    // CLSID_ShellLink.
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");

    public string? Create(string linkPath, string targetPath)
    {
        IShellLinkW? link = null;
        try
        {
            PInvoke.CoCreateInstance(CLSID_ShellLink, null, CLSCTX.CLSCTX_INPROC_SERVER, out link);

            unsafe
            {
                fixed (char* target = targetPath)
                {
                    link.SetPath(target);
                }
                string? workingDir = Path.GetDirectoryName(
                    targetPath.TrimEnd(Path.DirectorySeparatorChar)
                );
                if (!string.IsNullOrEmpty(workingDir))
                {
                    fixed (char* wd = workingDir)
                    {
                        link.SetWorkingDirectory(wd);
                    }
                }
            }

            ((IPersistFile)link).Save(linkPath, true);
            return null;
        }
        catch (Exception ex)
        {
            return Marshal.GetExceptionForHR(ex.HResult)?.Message ?? ex.Message;
        }
        finally
        {
            if (link != null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}
