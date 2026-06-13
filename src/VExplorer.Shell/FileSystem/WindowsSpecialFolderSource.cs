using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Resolves known folders and the user home via <see cref="Environment"/>, and
/// enumerates the profile "place" folders dynamically via
/// <c>IKnownFolderManager</c> (so redirected/OneDrive paths are picked up at
/// runtime) with environment clutter filtered out.
/// </summary>
public sealed class WindowsSpecialFolderSource : ISpecialFolderSource
{
    // CLSID_KnownFolderManager.
    private static readonly Guid CLSID_KnownFolderManager = new(
        "4df0c730-df9d-4ae3-9153-aa6b82e9795a"
    );

    // Leaf folder names that pass the profile-direct test but are clutter for a
    // file explorer's top level. Matched case-insensitively on the path leaf.
    private static readonly HashSet<string> ExcludedLeafNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "Contacts",
        "Links",
        "Searches",
        "Saved Games",
        "3D Objects",
        "Favorites",
        "AppData",
        "Application Data",
        "Cookies",
        "Recent",
        "SendTo",
        "Start Menu",
        "Templates",
        "NetHood",
        "PrintHood",
        "Local Settings",
    };

    public string GetHomeDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string? Resolve(KnownFolder folder)
    {
        Environment.SpecialFolder special = folder switch
        {
            KnownFolder.Home => Environment.SpecialFolder.UserProfile,
            KnownFolder.Desktop => Environment.SpecialFolder.DesktopDirectory,
            KnownFolder.Documents => Environment.SpecialFolder.MyDocuments,
            KnownFolder.Pictures => Environment.SpecialFolder.MyPictures,
            KnownFolder.Music => Environment.SpecialFolder.MyMusic,
            KnownFolder.Videos => Environment.SpecialFolder.MyVideos,
            // Downloads has no SpecialFolder enum value; derive it from home.
            KnownFolder.Downloads => Environment.SpecialFolder.UserProfile,
            _ => Environment.SpecialFolder.UserProfile,
        };

        string path = Environment.GetFolderPath(special);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (folder == KnownFolder.Downloads)
        {
            path = Path.Combine(path, "Downloads");
        }

        return path;
    }

    public IReadOnlyList<SpecialFolderEntry> EnumeratePlaces()
    {
        string home = GetHomeDirectory();
        if (string.IsNullOrEmpty(home))
        {
            return Array.Empty<SpecialFolderEntry>();
        }
        string homeTrim = home.TrimEnd(Path.DirectorySeparatorChar);
        string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        string? oneDriveTrim = string.IsNullOrEmpty(oneDrive)
            ? null
            : oneDrive.TrimEnd(Path.DirectorySeparatorChar);

        List<SpecialFolderEntry> entries = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // Home always leads the list. FOLDERID_Profile.
        entries.Add(
            new SpecialFolderEntry(
                "Home",
                homeTrim,
                new Guid("5E6C858F-0E22-4760-9AFE-EA3317B67173")
            )
        );
        seen.Add(homeTrim);

        try
        {
            CollectKnownFolders(homeTrim, oneDriveTrim, entries, seen);
        }
        catch
        {
            // A shell failure must never break the PC listing; Home alone is fine.
        }

        return entries;
    }

    private void CollectKnownFolders(
        string homeTrim,
        string? oneDriveTrim,
        List<SpecialFolderEntry> entries,
        HashSet<string> seen
    )
    {
        IKnownFolderManager? manager = null;
        try
        {
            PInvoke.CoCreateInstance(
                CLSID_KnownFolderManager,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                out manager
            );
            if (manager is null)
            {
                return;
            }

            Guid[] ids = GetFolderIds(manager);
            foreach (Guid id in ids)
            {
                string? path = TryResolveFolder(manager, id);
                if (path == null)
                {
                    continue;
                }

                string trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
                if (!IsProfilePlace(trimmed, homeTrim, oneDriveTrim))
                {
                    continue;
                }

                string leaf = Path.GetFileName(trimmed);
                if (leaf.Length == 0 || ExcludedLeafNames.Contains(leaf))
                {
                    continue;
                }
                if (!seen.Add(trimmed))
                {
                    continue;
                }

                entries.Add(new SpecialFolderEntry(leaf, trimmed, id));
            }
        }
        finally
        {
            if (manager != null)
            {
                Marshal.ReleaseComObject(manager);
            }
        }
    }

    private static unsafe Guid[] GetFolderIds(IKnownFolderManager manager)
    {
        Guid* raw = null;
        try
        {
            uint count = 0;
            manager.GetFolderIds(&raw, ref count);
            if (raw == null || count == 0)
            {
                return [];
            }
            Guid[] ids = new Guid[count];
            for (uint i = 0; i < count; i++)
            {
                ids[i] = raw[i];
            }
            return ids;
        }
        finally
        {
            if (raw != null)
            {
                Marshal.FreeCoTaskMem((nint)raw);
            }
        }
    }

    private static unsafe string? TryResolveFolder(IKnownFolderManager manager, Guid id)
    {
        IKnownFolder folder = null!;
        try
        {
            manager.GetFolder(&id, out folder);
            folder.GetCategory(out KF_CATEGORY category);
            if (category == KF_CATEGORY.KF_CATEGORY_VIRTUAL)
            {
                return null;
            }

            PWSTR p = default;
            folder.GetPath((uint)KNOWN_FOLDER_FLAG.KF_FLAG_DONT_VERIFY, &p);
            try
            {
                string path = p.Value != null ? p.ToString() : "";
                return string.IsNullOrEmpty(path) ? null : path;
            }
            finally
            {
                if (p.Value != null)
                {
                    Marshal.FreeCoTaskMem((nint)p.Value);
                }
            }
        }
        catch
        {
            // Folders without a path (or that fail to resolve) are simply skipped.
            return null;
        }
        finally
        {
            if (folder != null)
            {
                Marshal.ReleaseComObject(folder);
            }
        }
    }

    /// <summary>
    /// A "place" is the home directory's direct child, or a folder under the
    /// OneDrive root. This drops deep paths (AppData\…) and out-of-profile folders.
    /// </summary>
    private static bool IsProfilePlace(string trimmed, string homeTrim, string? oneDriveTrim)
    {
        string? parent = Path.GetDirectoryName(trimmed);
        if (parent != null && string.Equals(parent, homeTrim, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (
            oneDriveTrim != null
            && trimmed.StartsWith(
                oneDriveTrim + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }
        return false;
    }
}
