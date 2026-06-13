using VExplorer.Core.FileSystem;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Delegates the Properties dialog (<c>SHObjectProperties</c>) and the
/// "Open with" picker (<c>SHOpenWithDialog</c>) to the shell.
/// </summary>
public sealed class WindowsShellIntegration : IShellIntegration
{
    public bool ShowProperties(string path, nint ownerHwnd)
    {
        return PInvoke.SHObjectProperties(new HWND(ownerHwnd), SHOP_TYPE.SHOP_FILEPATH, path, null);
    }

    public unsafe bool ShowOpenWith(string path, nint ownerHwnd)
    {
        fixed (char* file = path)
        {
            OPENASINFO info = new()
            {
                pcszFile = file,
                pcszClass = default,
                oaifInFlags =
                    OPEN_AS_INFO_FLAGS.OAIF_ALLOW_REGISTRATION | OPEN_AS_INFO_FLAGS.OAIF_EXEC,
            };
            HRESULT hr = PInvoke.SHOpenWithDialog(new HWND(ownerHwnd), info);
            return hr.Succeeded;
        }
    }
}
