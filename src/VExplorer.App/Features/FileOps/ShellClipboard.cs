using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace VExplorer.App.Features.FileOps;

/// <summary>
/// Explorer-compatible clipboard for file paths. Uses CF_HDROP
/// (<see cref="DataObject.SetFileDropList"/>) plus the "Preferred DropEffect"
/// format so cut/copy interoperates with Windows Explorer in both directions.
/// </summary>
public static class ShellClipboard
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const uint DropEffectCopy = 1;
    private const uint DropEffectMove = 2;

    /// <summary>Place <paramref name="paths"/> on the clipboard as copy or cut (move).</summary>
    public static void SetPaths(IReadOnlyList<string> paths, bool cut)
    {
        DataObject data = new();
        StringCollection files = [];
        files.AddRange([.. paths]);
        data.SetFileDropList(files);

        MemoryStream effect = new(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy));
        data.SetData(PreferredDropEffect, effect);

        Clipboard.SetDataObject(data, copy: true);
    }

    /// <summary>Read file paths from the clipboard and whether they were cut (move).</summary>
    public static (IReadOnlyList<string> Paths, bool IsCut) GetPaths()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return ([], false);
        }

        List<string> paths = [.. Clipboard.GetFileDropList().Cast<string>()];

        bool isCut = false;
        IDataObject? data = Clipboard.GetDataObject();
        if (
            data?.GetDataPresent(PreferredDropEffect) == true
            && data.GetData(PreferredDropEffect) is MemoryStream ms
        )
        {
            byte[] buffer = new byte[4];
            ms.Position = 0;
            if (ms.Read(buffer, 0, 4) == 4)
            {
                isCut = (BitConverter.ToUInt32(buffer, 0) & DropEffectMove) != 0;
            }
        }

        return (paths, isCut);
    }

    /// <summary>Builds a CF_HDROP <see cref="DataObject"/> for drag &amp; drop.</summary>
    public static DataObject BuildDataObject(IReadOnlyList<string> paths)
    {
        DataObject data = new();
        StringCollection files = [];
        files.AddRange([.. paths]);
        data.SetFileDropList(files);
        return data;
    }

    public static void SetText(string text)
    {
        Clipboard.SetText(text);
    }

    public static void Clear()
    {
        Clipboard.Clear();
    }
}
