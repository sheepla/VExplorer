namespace VExplorer.Core.Commands;

/// <summary>
/// App-wide history of executed COMMAND-mode command lines, oldest first.
/// In-memory only; recalled in the command bar via Up/Down and Ctrl+P/Ctrl+N.
/// </summary>
public interface ICommandHistory
{
    /// <summary>Executed command lines, oldest first.</summary>
    IReadOnlyList<string> Entries { get; }

    /// <summary>Records a command line. Ignores blanks and consecutive duplicates.</summary>
    void Add(string commandLine);
}
