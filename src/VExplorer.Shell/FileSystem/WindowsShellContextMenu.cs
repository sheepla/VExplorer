using System.Diagnostics;
using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Extracts items from the Windows shell <c>IContextMenu</c> into the abstract
/// <see cref="ShellMenuItem"/> model (8章). The menu is never drawn by the shell:
/// we let it populate an off-screen <c>HMENU</c> via <c>QueryContextMenu</c>, read the
/// entries with <c>GetMenuItemInfoW</c>, and send the chosen command back with
/// <c>InvokeCommand</c> — so the WPF MENU mode can drive every entry with hjkl.
/// <para>
/// COM interop is hand-written here (classic <c>[ComImport]</c> + P/Invoke) rather than
/// via CsWin32: <c>IContextMenu.QueryContextMenu</c>/<c>InvokeCommand</c> work in raw
/// <c>HMENU</c> handles and a variable-size <c>CMINVOKECOMMANDINFOEX</c>, which the
/// generated COM marshalling does not express cleanly. Must run on the STA UI thread.
/// </para>
/// </summary>
public sealed class WindowsShellContextMenu : IShellContextMenu
{
    public IShellMenuSession? OpenForItems(IReadOnlyList<string> paths, nint ownerHwnd)
    {
        if (paths.Count == 0)
        {
            return null;
        }
        AssertSta();

        List<nint> pidls = [];
        try
        {
            foreach (string path in paths)
            {
                if (SHParseDisplayName(path, 0, out nint pidl, 0, out _) >= 0 && pidl != 0)
                {
                    pidls.Add(pidl);
                }
            }
            if (pidls.Count == 0)
            {
                return null;
            }

            Guid arrayIid = IID_IShellItemArray;
            if (
                SHCreateShellItemArrayFromIDLists((uint)pidls.Count, [.. pidls], out object arrObj)
                    < 0
                || arrObj is not IShellItemArray array
            )
            {
                return null;
            }

            try
            {
                Guid bhid = BHID_SFUIObject;
                Guid cmIid = IID_IContextMenu;
                if (array.BindToHandler(0, ref bhid, ref cmIid, out nint cmPtr) < 0 || cmPtr == 0)
                {
                    return null;
                }
                return BuildSession(cmPtr, ownerHwnd);
            }
            finally
            {
                Marshal.ReleaseComObject(array);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OpenForItems failed: {ex.Message}");
            return null;
        }
        finally
        {
            foreach (nint pidl in pidls)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    public IShellMenuSession? OpenForFolderBackground(string folderPath, nint ownerHwnd)
    {
        AssertSta();
        try
        {
            Guid itemIid = IID_IShellItem;
            if (
                SHCreateItemFromParsingName(folderPath, 0, ref itemIid, out object itemObj) < 0
                || itemObj is not IShellItem item
            )
            {
                return null;
            }

            IShellFolder? folder = null;
            try
            {
                Guid bhid = BHID_SFObject;
                Guid sfIid = IID_IShellFolder;
                if (item.BindToHandler(0, ref bhid, ref sfIid, out nint sfPtr) < 0 || sfPtr == 0)
                {
                    return null;
                }
                folder = (IShellFolder)Marshal.GetObjectForIUnknown(sfPtr);
                Marshal.Release(sfPtr);

                Guid cmIid = IID_IContextMenu;
                if (folder.CreateViewObject(ownerHwnd, ref cmIid, out nint cmPtr) < 0 || cmPtr == 0)
                {
                    return null;
                }
                return BuildSession(cmPtr, ownerHwnd);
            }
            finally
            {
                if (folder != null)
                {
                    Marshal.ReleaseComObject(folder);
                }
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OpenForFolderBackground failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Wraps a freshly bound IContextMenu pointer in a session and queries it.</summary>
    private static ShellMenuSession? BuildSession(nint cmPtr, nint ownerHwnd)
    {
        var cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
        Marshal.Release(cmPtr);

        nint hmenu = CreatePopupMenu();
        if (hmenu == 0)
        {
            Marshal.ReleaseComObject(cm);
            return null;
        }
        // CMF_NORMAL | CMF_EXTENDEDVERBS so "extended" (shift) verbs are included too.
        int hr = cm.QueryContextMenu(
            hmenu,
            0,
            IdCmdFirst,
            IdCmdLast,
            CMF_NORMAL | CMF_EXTENDEDVERBS
        );
        if (hr < 0)
        {
            DestroyMenu(hmenu);
            Marshal.ReleaseComObject(cm);
            return null;
        }
        return new ShellMenuSession(cm, hmenu, ownerHwnd);
    }

    private static void AssertSta()
    {
        Debug.Assert(
            Thread.CurrentThread.GetApartmentState() == ApartmentState.STA,
            "Shell context menu extraction must run on an STA thread."
        );
    }

    private sealed class ShellMenuSession : IShellMenuSession
    {
        private readonly record struct ItemRef(int ShellCmd, nint SubMenu);

        private readonly IContextMenu _cm;
        private readonly nint _hmenu;
        private readonly nint _ownerHwnd;
        private readonly Dictionary<int, ItemRef> _items = [];
        private int _nextToken;
        private bool _disposed;

        public ShellMenuSession(IContextMenu cm, nint hmenu, nint ownerHwnd)
        {
            _cm = cm;
            _hmenu = hmenu;
            _ownerHwnd = ownerHwnd;
            TopLevelItems = Walk(hmenu);
        }

        public IReadOnlyList<ShellMenuItem> TopLevelItems { get; }

        public IReadOnlyList<ShellMenuItem> ExpandSubmenu(int itemId)
        {
            return _items.TryGetValue(itemId, out ItemRef r) && r.SubMenu != 0
                ? Walk(r.SubMenu)
                : [];
        }

        public bool Invoke(int itemId)
        {
            if (_disposed || !_items.TryGetValue(itemId, out ItemRef r) || r.ShellCmd < 0)
            {
                return false;
            }
            try
            {
                CMINVOKECOMMANDINFOEX ici = new()
                {
                    cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE,
                    hwnd = _ownerHwnd,
                    lpVerb = r.ShellCmd, // MAKEINTRESOURCEA(cmd)
                    lpVerbW = r.ShellCmd, // MAKEINTRESOURCEW(cmd)
                    nShow = SW_SHOWNORMAL,
                };
                return _cm.InvokeCommand(ref ici) >= 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"InvokeCommand failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Reads one level of <paramref name="hmenu"/> into abstract items.</summary>
        private List<ShellMenuItem> Walk(nint hmenu)
        {
            List<ShellMenuItem> result = [];
            int count = GetMenuItemCount(hmenu);
            for (int i = 0; i < count; i++)
            {
                MENUITEMINFOW mii = new()
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                    fMask = MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_SUBMENU | MIIM_BITMAP,
                };
                if (!GetMenuItemInfoW(hmenu, (uint)i, true, ref mii))
                {
                    continue;
                }

                if ((mii.fType & MFT_SEPARATOR) != 0)
                {
                    result.Add(new ShellMenuItem { Text = "", IsSeparator = true });
                    continue;
                }

                bool ownerDrawn = (mii.fType & MFT_OWNERDRAW) != 0;
                string label = ownerDrawn ? "" : ReadLabel(hmenu, i);
                bool hasSub = mii.hSubMenu != 0;
                int shellCmd = hasSub ? -1 : (int)(mii.wID - IdCmdFirst);
                int token = _nextToken++;
                _items[token] = new ItemRef(shellCmd, mii.hSubMenu);

                result.Add(
                    new ShellMenuItem
                    {
                        Text = StripAccelerators(label),
                        HasSubmenu = hasSub,
                        IsDisabled = (mii.fState & MFS_GRAYED) != 0,
                        Id = token,
                        LabelMissing = label.Length == 0 && !hasSub,
                        // Skip HBMMENU_* sentinel values (small magic numbers, not real bitmaps).
                        IconHandle = mii.hbmpItem is > 32 ? mii.hbmpItem : 0,
                    }
                );
            }
            return result;
        }

        /// <summary>Two-pass read of a menu item's text (first call sizes the buffer).</summary>
        private static string ReadLabel(nint hmenu, int index)
        {
            MENUITEMINFOW probe = new()
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                fMask = MIIM_STRING,
            };
            if (!GetMenuItemInfoW(hmenu, (uint)index, true, ref probe) || probe.cch == 0)
            {
                return "";
            }
            uint cch = probe.cch + 1; // cch excludes the terminating NUL
            nint buffer = Marshal.AllocHGlobal((int)cch * 2);
            try
            {
                MENUITEMINFOW read = new()
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                    fMask = MIIM_STRING,
                    dwTypeData = buffer,
                    cch = cch,
                };
                return GetMenuItemInfoW(hmenu, (uint)index, true, ref read)
                    ? Marshal.PtrToStringUni(buffer) ?? ""
                    : "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Removes mnemonic <c>&amp;</c> markers ("Cu&amp;t" → "Cut", "&amp;&amp;" → "&amp;").</summary>
        private static string StripAccelerators(string text)
        {
            if (!text.Contains('&'))
            {
                return text;
            }
            System.Text.StringBuilder sb = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '&')
                {
                    if (i + 1 < text.Length && text[i + 1] == '&')
                    {
                        sb.Append('&');
                        i++;
                    }
                    // else: drop the single '&'
                }
                else
                {
                    sb.Append(text[i]);
                }
            }
            return sb.ToString().Trim();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_hmenu != 0)
            {
                DestroyMenu(_hmenu); // submenus are destroyed with the parent
            }
            Marshal.ReleaseComObject(_cm);
        }
    }

    // ── Native interop ─────────────────────────────────────────────────────────

    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;
    private const uint CMF_NORMAL = 0x0;
    private const uint CMF_EXTENDEDVERBS = 0x100;
    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const int SW_SHOWNORMAL = 1;

    private const uint MIIM_STATE = 0x1;
    private const uint MIIM_ID = 0x2;
    private const uint MIIM_SUBMENU = 0x4;
    private const uint MIIM_STRING = 0x40;
    private const uint MIIM_BITMAP = 0x80;
    private const uint MIIM_FTYPE = 0x100;
    private const uint MFT_SEPARATOR = 0x800;
    private const uint MFT_OWNERDRAW = 0x100;
    private const uint MFS_GRAYED = 0x3; // MF_GRAYED | MF_DISABLED

    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IShellItemArray = new("b63ea76d-1f85-456f-a19c-48159efa858b");
    private static readonly Guid IID_IShellFolder = new("000214e6-0000-0000-c000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    private static readonly Guid BHID_SFObject = new("3981e228-f559-11d3-8e3a-00c04f6837d5");
    private static readonly Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(
        string pszName,
        nint pbc,
        out nint ppidl,
        uint sfgaoIn,
        out uint psfgaoOut
    );

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromIDLists(
        uint cidl,
        nint[] rgpidl,
        [MarshalAs(UnmanagedType.Interface)] out object ppsiItemArray
    );

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        nint pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv
    );

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint pv);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMenuItemInfoW(
        nint hMenu,
        uint item,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition,
        ref MENUITEMINFOW lpmii
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFOW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nint dwItemData;
        public nint dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpTitle;
        public nint lpVerbW;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParametersW;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectoryW;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTitleW;
        public int ptX;
        public int ptY;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);

        [PreserveSig]
        int GetParent(out nint ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out nint ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(nint psi, uint hint, out int piOrder);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    private interface IShellItemArray
    {
        [PreserveSig]
        int BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppvOut);
        // Remaining methods (GetPropertyStore, …, EnumItems) are unused — not declared.
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214e6-0000-0000-c000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName();

        [PreserveSig]
        int EnumObjects();

        [PreserveSig]
        int BindToObject();

        [PreserveSig]
        int BindToStorage();

        [PreserveSig]
        int CompareIDs();

        [PreserveSig]
        int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);
        // GetAttributesOf, GetUIObjectOf, GetDisplayNameOf, SetNameOf are unused.
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(
            nint hmenu,
            uint indexMenu,
            uint idCmdFirst,
            uint idCmdLast,
            uint uFlags
        );

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);
    }
}
