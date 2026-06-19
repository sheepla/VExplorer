using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;
using Windows.Win32;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Win32-backed <see cref="IDragOverlayInterop"/>: cursor position via
/// <c>GetCursorPos</c> and hit-test transparency via the window's extended
/// style (<c>WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW</c>).
/// </summary>
public sealed class WindowsDragOverlayInterop : IDragOverlayInterop
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    public bool TryGetCursorPosition(out int x, out int y)
    {
        if (PInvoke.GetCursorPos(out System.Drawing.Point pt))
        {
            x = pt.X;
            y = pt.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    public void MakeHitTestTransparent(nint hwnd)
    {
        nint exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(
            hwnd,
            GWL_EXSTYLE,
            exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW
        );
    }

    // CsWin32 cannot generate GetWindowLongPtr/SetWindowLongPtr for an AnyCPU
    // target (the Win32 headers define them as bitness-dependent macros), so
    // these fall back to hand-written DllImports with a runtime bitness switch.
    private static nint GetWindowLongPtr(nint hWnd, int index) =>
        nint.Size == 8 ? GetWindowLongPtrW(hWnd, index) : GetWindowLongW(hWnd, index);

    private static nint SetWindowLongPtr(nint hWnd, int index, nint value) =>
        nint.Size == 8 ? SetWindowLongPtrW(hWnd, index, value) : SetWindowLongW(hWnd, index, value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern nint GetWindowLongW(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern nint SetWindowLongW(nint hWnd, int index, nint value);
}
