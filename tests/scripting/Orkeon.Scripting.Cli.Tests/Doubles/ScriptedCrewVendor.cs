using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Rag.Pipeline;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ILlmProvider"/> answering every kind of call a hierarchical crew
/// with planning and a RAG tool makes — the plan, the manager's assignment and review, the
/// agent's turns, the RAG pipeline's grounded answer — recognised from the prompt, each with
/// its own usage. It keeps the kind of every call it answered and the tokens it reported: the
/// ground truth a token meter is checked against.
/// </summary>
internal sealed class ScriptedCrewVendor : ILlmProvider
{
    private readonly Lock _gate = new();
    private readonly List<string> _answered = [];
    private long _totalTokens;

    /// <inheritdoc />
    public string Name => "scripted-vendor";

    /// <summary>The task the plan and the assignment name.</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>The agent the plan and the assignment pick.</summary>
    public string WorkerId { get; set; } = string.Empty;

    /// <summary>The kind of every call answered, in order.</summary>
    public IReadOnlyList<string> Answered
    {
        get { lock (_gate) return [.. _answered]; }
    }

    /// <summary>How many calls were answered.</summary>
    public int Calls => Answered.Count;

    /// <summary>Every token reported, both directions.</summary>
    public long TotalTokens
    {
        get { lock (_gate) return _totalTokens; }
    }

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return Task.FromResult(prompt.StartsWith("You are planning the execution", StringComparison.Ordinal)
            ? Answer("planning", $$"""{"tasks":[{"task":"{{TaskId}}","order":1,"agent":"{{WorkerId}}"}]}""", 101, 11)
            : Answer("generate", "generated", 100, 10));
    }

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var text = string.Join('\n', messages.Select(m => m.Content));

        if (text.Contains("responsible for assigning tasks", StringComparison.Ordinal))
            return Task.FromResult(Answer("assign", $$"""{"agent_id":"{{WorkerId}}","reason":"knows the knowledge base"}""", 102, 12));
        if (text.Contains("reviewing the output of a completed task", StringComparison.Ordinal))
            return Task.FromResult(Answer("review", """{"approved":true,"feedback":""}""", 105, 15));
        if (messages.Length > 0 && messages[0].Content == StagedRagPipeline.DefaultSystemPrompt)
            return Task.FromResult(Answer("rag", "The warranty lasts two years [1].", 104, 14));
        if (messages.Any(m => m.Role == "tool"))
            return Task.FromResult(Answer("agent", "The warranty lasts two years.", 103, 13));

        return Task.FromResult(Answer(
            "agent",
            string.Empty,
            103,
            13,
            raw: """{"choices":[{"message":{"role":"assistant","content":"","tool_calls":[{"id":"call-1","type":"function","function":{"name":"rag_search","arguments":"{\"question\":\"How long is the warranty?\"}"}}]}}]}"""));
    }

    private LlmResponse Answer(string kind, string content, int prompt, int completion, string? raw = null)
    {
        lock (_gate)
        {
            _answered.Add(kind);
            _totalTokens += prompt + completion;
        }

        return new LlmResponse
        {
            Content = content,
            RawResponseBody = raw,
            PromptTokens = prompt,
            CompletionTokens = completion,
            TokensUsed = prompt + completion,
            Model = "vendor/model-x",
        };
    }
}
