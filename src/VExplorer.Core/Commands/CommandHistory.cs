namespace VExplorer.Core.Commands;

/// <summary>
/// In-memory <see cref="ICommandHistory"/>. Trims to a cap and collapses a
/// command that repeats the immediately previous one.
/// </summary>
public sealed class CommandHistory : ICommandHistory
{
    private const int MaxEntries = 200;

    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string commandLine)
    {
        string line = commandLine.Trim();
        if (line.Length == 0)
        {
            return;
        }
        if (_entries.Count > 0 && string.Equals(_entries[^1], line, StringComparison.Ordinal))
        {
            return;
        }
        _entries.Add(line);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
        }
    }
}
