using System.Collections.Concurrent;
using Orkeon.Application.Crew.Compaction;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>An <see cref="IConversationPruner"/> that prunes nothing and keeps the requests it is asked.</summary>
internal sealed class FakeConversationPruner : IConversationPruner
{
    /// <summary>The requests received, oldest first.</summary>
    public ConcurrentQueue<ConversationPruneRequest> Requests { get; } = new();

    /// <inheritdoc />
    public ValueTask<ConversationPruneResponse?> PruneAsync(
        ConversationPruneRequest request, ActConversationView conversation, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return ValueTask.FromResult<ConversationPruneResponse?>(null);
    }
}
