using System.Runtime.CompilerServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Core.Completion;

/// <summary>
/// Completes filesystem paths. Splits the token into directory + leaf, lists
/// the directory (always including hidden/system entries), filters by the leaf
/// and returns candidates sorted folders-first then alphabetically. Folder
/// candidates carry a trailing separator so partial-input + Tab chains downward
/// (e.g. <c>C:\U</c> → <c>C:\Users\</c>).
/// </summary>
public sealed class PathCompletionProvider(
    IPathCompletionSource source,
    ISpecialFolderSource specialFolders
) : ICompletionProvider
{
    private readonly IPathCompletionSource _source = source;
    private readonly ISpecialFolderSource _specialFolders = specialFolders;

    public CompletionContextKind Kind => CompletionContextKind.Path;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        PathTokenSplit split = PathTokenSplitter.Split(
            context.Token,
            context.CurrentDirectory,
            _specialFolders.GetHomeDirectory()
        );

        List<CompletionCandidate> matches = [];
        await foreach (PathEntry entry in _source.EnumerateAsync(split.DirectoryPath, cancel))
        {
            if (!CompletionMatcher.IsMatch(entry.Name, split.LeafPrefix))
            {
                continue;
            }

            string insertion = entry.IsDirectory
                ? split.DirPrefix + entry.Name + Path.DirectorySeparatorChar
                : split.DirPrefix + entry.Name;

            matches.Add(
                new CompletionCandidate(
                    insertion,
                    entry.Name,
                    entry.IsDirectory ? CompletionKind.Folder : CompletionKind.File
                )
            );
        }

        // Folders first, then alphabetical within each group (stable, predictable).
        matches.Sort(
            (a, b) =>
            {
                bool aDir = a.Kind == CompletionKind.Folder;
                bool bDir = b.Kind == CompletionKind.Folder;
                if (aDir != bDir)
                {
                    return aDir ? -1 : 1;
                }
                return string.Compare(
                    a.DisplayName,
                    b.DisplayName,
                    StringComparison.OrdinalIgnoreCase
                );
            }
        );

        foreach (CompletionCandidate candidate in matches)
        {
            yield return candidate;
        }
    }
}
