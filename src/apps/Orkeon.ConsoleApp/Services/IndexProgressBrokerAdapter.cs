using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Cli.Scripting.Progress;

namespace Orkeon.ConsoleApp.Services;

/// <summary>
/// Routes the codebase indexer's build notifications (<see cref="IndexBuildProgress"/>)
/// into the CLI <see cref="ProgressBroker"/>, so a long <c>index_codebase</c> run drives
/// the TUI status-line bar like any scripted operation.
/// </summary>
/// <remarks>
/// Implements <see cref="IProgress{T}"/> directly (no <see cref="Progress{T}"/> base):
/// the BCL class marshals through the captured SynchronizationContext, which under
/// Terminal.Gui would queue every per-file tick onto the UI loop — the broker is already
/// thread-safe and the UI polls it, so reports must stay on the calling thread.
/// </remarks>
internal sealed class IndexProgressBrokerAdapter : IProgress<IndexBuildProgress>
{
    private const string Label = "Indexing codebase";

    private readonly ProgressBroker _broker;

    public IndexProgressBrokerAdapter(ProgressBroker broker)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public void Report(IndexBuildProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var counted = value.Total > 0;
        _broker.Report(new ProgressSnapshot
        {
            Label = Label,
            Step = counted ? value.Current : null,
            Total = counted ? value.Total : null,
            Message = Describe(value.Phase),
            StartedAt = DateTimeOffset.UtcNow,
            Ticket = ProgressAmbient.CurrentInstance?.Ticket,
        });
    }

    private static string Describe(IndexBuildPhase phase) => phase switch
    {
        IndexBuildPhase.Discovery => "discovering files",
        IndexBuildPhase.Parse => "parsing sources",
        IndexBuildPhase.Resolve => "resolving dependencies",
        IndexBuildPhase.Enrich => "summarizing nodes",
        IndexBuildPhase.Embed => "embedding nodes",
        IndexBuildPhase.Persist => "persisting vectors",
        _ => "indexing",
    };
}
