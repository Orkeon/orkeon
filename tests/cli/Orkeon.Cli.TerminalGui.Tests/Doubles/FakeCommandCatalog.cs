using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.TerminalGui.Tests.Doubles;

/// <summary>A completion source that also lists its commands, and counts how often it is asked.</summary>
internal sealed class FakeCommandCatalog : IReplInputAssist, IReplCommandCatalog
{
    public IReadOnlyList<ReplCommandEntry> Entries { get; set; } = [];

    /// <summary>How many times the commands were listed.</summary>
    public int ListCalls { get; private set; }

    public IReadOnlyList<ReplCommandEntry> ListCommands()
    {
        ListCalls++;
        return Entries;
    }

    public IReadOnlyList<string> CompleteCommand(string prefix)
        => [.. Entries.Select(e => e.Name).Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];

    public IReadOnlyList<string> CompletePath(string prefix) => [];
}
