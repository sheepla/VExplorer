using System.Globalization;
using System.Runtime.InteropServices;
using VExplorer.Core.FileSystem;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace VExplorer.Shell.FileSystem;

/// <summary>
/// Reads and operates on the Recycle Bin via the late-bound <c>Shell.Application</c>
/// automation object (the classic, robust path — far simpler than driving
/// <c>IShellFolder</c>/PIDLs by hand). Items are matched across calls by their
/// backing path (the unique <c>$R…</c> file under <c>$Recycle.Bin</c>), which the
/// source remembers per token so restore / delete can re-find the live item.
/// </summary>
public sealed class RecycleBinShell : IRecycleBinSource
{
    // ssfBITBUCKET: the Recycle Bin special folder for Shell.NameSpace.
    private const int SsfBitBucket = 10;

    // Recycle Bin detail columns (stable on Windows 10/11).
    private const int ColOriginalLocation = 1;
    private const int ColDateDeleted = 2;

    // token -> backing path of the deleted item ($R file/folder).
    private readonly Dictionary<int, string> _byToken = [];

    public IReadOnlyList<RecycleBinItem> List()
    {
        _byToken.Clear();
        List<RecycleBinItem> result = [];

        dynamic? shell = CreateShell();
        if (shell == null)
        {
            return result;
        }
        try
        {
            dynamic bin = shell.NameSpace(SsfBitBucket);
            if (bin == null)
            {
                return result;
            }
            dynamic items = bin.Items();
            int token = 0;
            foreach (dynamic item in items)
            {
                string name = SafeStr(() => (string)item.Name);
                string backingPath = SafeStr(() => (string)item.Path);
                string original = SafeStr(() =>
                    (string)bin.GetDetailsOf(item, ColOriginalLocation)
                );
                string deletedRaw = SafeStr(() => (string)bin.GetDetailsOf(item, ColDateDeleted));
                bool isFolder = SafeBool(() => (bool)item.IsFolder);
                long? size = isFolder ? null : SafeSize(() => (long)item.Size);

                _byToken[token] = backingPath;
                result.Add(
                    new RecycleBinItem(
                        token,
                        name,
                        original,
                        ParseDeletedDate(deletedRaw),
                        size,
                        isFolder
                    )
                );
                token++;
            }
        }
        catch
        {
            // Enumeration failures yield whatever was collected so far.
        }
        finally
        {
            Release(shell);
        }
        return result;
    }

    public void Restore(IReadOnlyList<int> tokens)
    {
        HashSet<string> targets = ResolvePaths(tokens);
        if (targets.Count == 0)
        {
            return;
        }

        dynamic? shell = CreateShell();
        if (shell == null)
        {
            return;
        }
        try
        {
            dynamic bin = shell.NameSpace(SsfBitBucket);
            foreach (dynamic item in bin.Items())
            {
                string backingPath = SafeStr(() => (string)item.Path);
                if (!targets.Contains(backingPath))
                {
                    continue;
                }
                InvokeRestore(item);
            }
        }
        catch
        {
            // Best effort; leave un-restored items in place.
        }
        finally
        {
            Release(shell);
        }
    }

    public void DeletePermanently(IReadOnlyList<int> tokens, nint ownerHwnd)
    {
        // The backing $R path is a real filesystem path; a non-recycling shell
        // delete removes it from the bin permanently.
        HashSet<string> targets = ResolvePaths(tokens);
        foreach (string path in targets)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Skip items that cannot be removed (in use / access denied).
            }
        }
    }

    public unsafe void Empty(nint ownerHwnd)
    {
        // SHERB_NOCONFIRMATION(1) | SHERB_NOPROGRESSUI(2) | SHERB_NOSOUND(4) = 7
        // Leave confirmation to the caller's UI; pass 0 to show the OS prompt.
        PInvoke.SHEmptyRecycleBin(new HWND(ownerHwnd), null, 0);
        _byToken.Clear();
    }

    private HashSet<string> ResolvePaths(IReadOnlyList<int> tokens)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        foreach (int t in tokens)
        {
            if (_byToken.TryGetValue(t, out string? path) && !string.IsNullOrEmpty(path))
            {
                set.Add(path);
            }
        }
        return set;
    }

    private static dynamic? CreateShell()
    {
        Type? t = Type.GetTypeFromProgID("Shell.Application");
        return t == null ? null : Activator.CreateInstance(t);
    }

    private static void Release(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    /// <summary>Invokes the localized "Restore" verb on a deleted item.</summary>
    private static void InvokeRestore(dynamic item)
    {
        foreach (dynamic verb in item.Verbs())
        {
            string name = SafeStr(() => (string)verb.Name);
            // The restore verb is localized; match common forms and the canonical
            // ampersand-accelerated names. Falls through harmlessly if absent.
            string normalized = name.Replace("&", "").Trim();
            if (
                normalized.Equals("Restore", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("元に戻す", StringComparison.Ordinal)
                || normalized.Contains("restore", StringComparison.OrdinalIgnoreCase)
            )
            {
                verb.DoIt();
                return;
            }
        }
    }

    private static DateTime? ParseDeletedDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        // Strip left-to-right marks the shell inserts into detail strings.
        string cleaned = new(raw.Where(c => !char.IsControl(c) && c != '‎' && c != '‏').ToArray());
        return DateTime.TryParse(
            cleaned,
            CultureInfo.CurrentCulture,
            DateTimeStyles.None,
            out DateTime dt
        )
            ? dt.ToUniversalTime()
            : null;
    }

    private static string SafeStr(Func<string> get)
    {
        try
        {
            return get() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool SafeBool(Func<bool> get)
    {
        try
        {
            return get();
        }
        catch
        {
            return false;
        }
    }

    private static long? SafeSize(Func<long> get)
    {
        try
        {
            return get();
        }
        catch
        {
            return null;
        }
    }
}
