using System.Collections.Immutable;

namespace VExplorer.Core.Completion;

/// <summary>
/// Classifies the ADDRESS-bar buffer into a completion context. The whole
/// buffer is treated as a single path token served by the Path and
/// SpecialFolder providers.
/// <para>
/// A COMMAND-bar resolver will be added separately; it is intentionally not
/// abstracted behind an interface until a second implementation exists.
/// </para>
/// </summary>
public sealed class AddressContextResolver
{
    private static readonly ImmutableArray<CompletionContextKind> AddressProviders =
    [
        CompletionContextKind.NavigationHistory,
        CompletionContextKind.UncPath,
        CompletionContextKind.Path,
        CompletionContextKind.SpecialFolder,
    ];

    public CompletionContext Resolve(string buffer, int caretIndex, string currentDirectory)
    {
        int caret = Math.Clamp(caretIndex, 0, buffer.Length);
        return new CompletionContext(
            Buffer: buffer,
            CaretIndex: caret,
            Token: buffer[..caret],
            TokenStart: 0,
            CurrentDirectory: currentDirectory,
            Providers: AddressProviders
        );
    }
}
