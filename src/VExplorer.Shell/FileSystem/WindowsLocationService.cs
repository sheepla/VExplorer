using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Windows implementation of <see cref="ILocationService"/>. Builds the PC root
/// (drives + special folders + Recycle Bin + Network), derives parents, and maps
/// special "place" folders to their physical path so the physical lister can be
/// reused for their contents.
/// </summary>
public sealed class WindowsLocationService(ISpecialFolderSource specialFolders) : ILocationService
{
    private readonly ISpecialFolderSource _specialFolders = specialFolders;

    public IFileItemSource ListPcRoot()
    {
        List<FileItem> items = [];

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }
            items.Add(
                new FileItem
                {
                    Name = drive.Name,
                    FullPath = drive.RootDirectory.FullName,
                    IsDirectory = true,
                    Kind = FileItemKind.Drive,
                    // Drives are physical; Identity stays null (resolves to the path).
                }
            );
        }

        // Special "place" folders: shell-namespace items whose contents are physical
        // and whose parent is PC. Identity carries the physical path as its parsing
        // name plus the KNOWNFOLDERID for icon resolution.
        foreach (SpecialFolderEntry place in _specialFolders.EnumeratePlaces())
        {
            items.Add(
                new FileItem
                {
                    Name = place.DisplayName,
                    DisplayName = place.DisplayName,
                    FullPath = place.PhysicalPath,
                    IsDirectory = true,
                    Kind = FileItemKind.Alias,
                    Identity = Location.ForShell(
                        place.PhysicalPath,
                        place.DisplayName,
                        place.KnownFolderId
                    ),
                }
            );
        }

        items.Add(
            new FileItem
            {
                Name = KnownLocations.RecycleBin.DisplayName,
                DisplayName = KnownLocations.RecycleBin.DisplayName,
                FullPath = "",
                IsDirectory = true,
                Kind = FileItemKind.Virtual,
                Identity = KnownLocations.RecycleBin,
            }
        );
        items.Add(
            new FileItem
            {
                Name = KnownLocations.Network.DisplayName,
                DisplayName = KnownLocations.Network.DisplayName,
                FullPath = "",
                IsDirectory = true,
                Kind = FileItemKind.Virtual,
                Identity = KnownLocations.Network,
            }
        );

        return new InMemoryFileItemSource(items);
    }

    public Location? GetParent(Location location)
    {
        if (KnownLocations.IsPc(location))
        {
            return null;
        }

        // Shell items (special folders, Recycle Bin, Network) live directly under PC.
        if (location.IsShell)
        {
            return KnownLocations.Pc;
        }

        // Filesystem: physical parent, or PC when at a drive/UNC root.
        if (location.TryGetFileSystemPath(out string path))
        {
            string? parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(parent) ? KnownLocations.Pc : Location.ForPath(parent);
        }

        return KnownLocations.Pc;
    }

    public bool TryGetListingPath(Location location, out string physicalPath)
    {
        if (location.TryGetFileSystemPath(out physicalPath))
        {
            return true;
        }

        // Special "place" folders carry their physical path as the parsing name.
        if (
            location.IsShell
            && !KnownLocations.IsPc(location)
            && !KnownLocations.IsRecycleBin(location)
            && !KnownLocations.IsNetwork(location)
        )
        {
            string parsing = location.ParsingName;
            if (Path.IsPathFullyQualified(parsing))
            {
                physicalPath = parsing;
                return true;
            }
        }

        physicalPath = "";
        return false;
    }

    private static readonly Location[] _knownRoots =
    [
        KnownLocations.Pc,
        KnownLocations.RecycleBin,
        KnownLocations.Network,
    ];

    public bool TryResolve(string input, out Location location)
    {
        location = default;
        string s = input.Trim().Trim('"');
        if (s.Length == 0)
        {
            return false;
        }

        // Known location names / parsing names (PC, Recycle Bin, Network).
        foreach (Location known in _knownRoots)
        {
            if (
                s.Equals(known.DisplayName, StringComparison.OrdinalIgnoreCase)
                || s.Equals(known.ParsingName, StringComparison.OrdinalIgnoreCase)
            )
            {
                location = known;
                return true;
            }
        }

        // An existing filesystem directory.
        if (Directory.Exists(s))
        {
            location = Location.ForPath(s);
            return true;
        }

        // A Windows shell parsing name (shell:Documents, ::{CLSID}).
        if (
            s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("::{", StringComparison.Ordinal)
        )
        {
            if (TryShellNameToPath(s, out string physical))
            {
                location = Location.ForPath(physical);
                return true;
            }
            // Resolves to a virtual item (no filesystem path); navigate best-effort.
            location = Location.ForShell(s, s);
            return true;
        }

        return false;
    }

    /// <summary>Resolves a shell parsing name to its filesystem path, when it has one.</summary>
    private static bool TryShellNameToPath(string parsingName, out string path)
    {
        path = "";
        if (SHParseDisplayName(parsingName, 0, out nint pidl, 0, out _) != 0 || pidl == 0)
        {
            return false;
        }
        try
        {
            char[] buffer = new char[260];
            if (SHGetPathFromIDListW(pidl, buffer))
            {
                int len = Array.IndexOf(buffer, '\0');
                string result = new(buffer, 0, len < 0 ? buffer.Length : len);
                if (result.Length > 0)
                {
                    path = result;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName,
        nint pbc,
        out nint ppidl,
        uint sfgaoIn,
        out uint psfgaoOut
    );

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(nint pidl, [Out] char[] pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint pv);
}
