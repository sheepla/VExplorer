using System.Runtime.CompilerServices;
using VExplorer.Core.FileSystem;

namespace VExplorer.Core.Completion;

/// <summary>
/// Completes Windows known folders by name (Home, Documents, …). The display
/// name → <see cref="KnownFolder"/> table lives here; physical paths are
/// resolved through <see cref="ISpecialFolderSource"/> so the Core stays free
/// of direct environment access.
/// </summary>
public sealed class SpecialFolderCompletionProvider(ISpecialFolderSource specialFolders)
    : ICompletionProvider
{
    private readonly ISpecialFolderSource _specialFolders = specialFolders;

    // Display labels are English per the project UI-text guideline. Order is
    // the table order; final ordering is decided by the engine/sort.
    private static readonly (string Name, KnownFolder Folder)[] Folders =
    [
        ("Home", KnownFolder.Home),
        ("Desktop", KnownFolder.Desktop),
        ("Documents", KnownFolder.Documents),
        ("Downloads", KnownFolder.Downloads),
        ("Pictures", KnownFolder.Pictures),
        ("Music", KnownFolder.Music),
        ("Videos", KnownFolder.Videos),
    ];

    public CompletionContextKind Kind => CompletionContextKind.SpecialFolder;

    public async IAsyncEnumerable<CompletionCandidate> GetCandidatesAsync(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancel
    )
    {
        await Task.CompletedTask; // keep the async-iterator signature uniform

        foreach ((string name, KnownFolder folder) in Folders)
        {
            cancel.ThrowIfCancellationRequested();

            if (!CompletionMatcher.IsMatch(name, context.Token))
            {
                continue;
            }

            string? path = _specialFolders.Resolve(folder);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            string insertion = path[^1] is '\\' or '/' ? path : path + Path.DirectorySeparatorChar;
            yield return new CompletionCandidate(insertion, name, CompletionKind.SpecialFolder);
        }
    }
}
