using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>Resolves every window to the one the test sets, and records what it was asked.</summary>
internal sealed class FakeContextWindowResolver(ContextWindow window) : IContextWindowResolver
{
    public List<(string Profile, string? Model, int? ProfileWindowTokens)> Calls { get; } = [];

    public ContextWindow Resolve(string profile, string? model, int? profileWindowTokens)
    {
        Calls.Add((profile, model, profileWindowTokens));
        return window;
    }
}

/// <summary>Records every compaction notice.</summary>
internal sealed class FakeCompactionObserver : IAgentCompactionObserver
{
    public List<AgentCompactionNotice> Notices { get; } = [];

    public void Compacted(AgentCompactionNotice notice) => Notices.Add(notice);
}

/// <summary>
/// A pressure stage that, once the conversation fills <see cref="FromShare"/> of the usable window,
/// replaces every tool result with <see cref="Replacement"/>; records each measure it is handed.
/// </summary>
internal sealed class FakeContextPressureStage : IContextPressureStage
{
    public double FromShare { get; init; } = 0.5;

    public string Replacement { get; init; } = "pruned";

    public List<ContextPressure> Pressures { get; } = [];

    public ValueTask<bool> RelieveAsync(ContextPressure pressure, ConversationView conversation, CancellationToken cancellationToken)
    {
        Pressures.Add(pressure);
        if (pressure.FillShare < FromShare)
            return ValueTask.FromResult(false);

        var changed = false;
        for (var i = 0; i < conversation.Count; i++)
        {
            if (conversation.RoleAt(i) == ConversationView.ToolRole && conversation.TextAt(i) != Replacement)
                changed |= conversation.ReplaceToolResult(i, Replacement);
        }
        return ValueTask.FromResult(changed);
    }
}

/// <summary>
/// A chat client for the compaction tests: the agent's turns call <see cref="ToolName"/>
/// <see cref="ToolTurns"/> times, then answer; a summary call (its system prompt is the compaction's)
/// answers <see cref="SummaryText"/> with <see cref="SummaryTokens"/>, or throws when
/// <see cref="SummaryFails"/>. Every request is recorded, the summary calls apart.
/// </summary>
internal sealed class FakeCompactionChatClient : IChatClient
{
    private int _turn;

    public required string ToolName { get; init; }

    public required int ToolTurns { get; init; }

    public string SummaryText { get; init; } = "The agent called the tool and read its results.";

    public int SummaryTokens { get; init; } = 50;

    public bool SummaryFails { get; init; }

    public List<List<ChatMessage>> AgentRequests { get; } = [];

    public List<List<ChatMessage>> SummaryRequests { get; } = [];

    public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = messages.ToList();
        if (request.Count == 2 && request[0].Text == CompactionPrompts.Instructions)
        {
            SummaryRequests.Add(request);
            if (SummaryFails)
                throw new InvalidOperationException("summary endpoint down");
            return System.Threading.Tasks.Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, SummaryText)])
            {
                Usage = new UsageDetails { TotalTokenCount = SummaryTokens },
            });
        }

        AgentRequests.Add(request);
        var turn = ++_turn;
        var message = turn <= ToolTurns
            ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{turn}", ToolName, arguments: null)])
            : new ChatMessage(ChatRole.Assistant, "done");
        return System.Threading.Tasks.Task.FromResult(new ChatResponse([message]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
