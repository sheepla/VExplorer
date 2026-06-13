namespace VExplorer.Core.Completion;

/// <summary>
/// The category of a single completion candidate. Drives the popup icon and,
/// for paths, whether a trailing separator is appended on accept.
/// </summary>
public enum CompletionKind
{
    Folder,
    File,
    SpecialFolder,

    // Reserved for COMMAND-mode completion (not produced yet).
    Command,
    SetOption,
    ExternalCommand,
}
