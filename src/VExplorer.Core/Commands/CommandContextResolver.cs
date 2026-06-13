using System.Collections.Immutable;
using VExplorer.Core.Completion;

namespace VExplorer.Core.Commands;

/// <summary>
/// Classifies the COMMAND-bar buffer (the text after <c>:</c>) into a completion
/// context. The first token completes as a command name; later tokens complete
/// according to the command's argument metadata (see <see cref="CommandSpec"/>).
/// Tokens are split on single spaces.
/// </summary>
public sealed class CommandContextResolver(CommandRegistry registry)
{
    private static readonly ImmutableArray<CompletionContextKind> NameOnly =
    [
        CompletionContextKind.CommandName,
    ];

    private readonly CommandRegistry _registry = registry;

    public CompletionContext Resolve(string buffer, int caretIndex, string currentDirectory)
    {
        int caret = Math.Clamp(caretIndex, 0, buffer.Length);
        string head = buffer[..caret];
        int spaceCount = head.Count(c => c == ' ');

        // First token → command name.
        if (spaceCount == 0)
        {
            return new CompletionContext(
                Buffer: buffer,
                CaretIndex: caret,
                Token: head,
                TokenStart: 0,
                CurrentDirectory: currentDirectory,
                Providers: NameOnly
            );
        }

        // Subsequent token → command argument. Look up the command's metadata to
        // decide which provider serves this argument position.
        int firstSpace = buffer.IndexOf(' ');
        string name = buffer[..firstSpace];
        int argPosition = spaceCount - 1;

        CommandSpec? spec = _registry.Find(name);
        CompletionContextKind? kind = spec?.ArgumentKindAt(argPosition);

        int tokenStart = head.LastIndexOf(' ') + 1;
        ImmutableArray<CompletionContextKind> providers = kind is { } k
            ? [k]
            : ImmutableArray<CompletionContextKind>.Empty;

        return new CompletionContext(
            Buffer: buffer,
            CaretIndex: caret,
            Token: buffer[tokenStart..caret],
            TokenStart: tokenStart,
            CurrentDirectory: currentDirectory,
            Providers: providers,
            CommandName: name,
            ArgumentIndex: argPosition
        );
    }
}
