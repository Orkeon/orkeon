using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A vendor that answers every call a crew run makes — the plan, the manager's assignment (to the
/// first agent it lists) and review (approved), an agent's turn — and records each one: what it
/// was, whose it was (the usage scope's agent) and when, on the clock it is given. A test that
/// holds a kind of call sets <see cref="Hold"/>: the call then waits on it, with its own token.
/// </summary>
public sealed partial class ClockedLlmVendor(Func<TimeSpan> now, string name = "clocked-vendor") : ILlmProvider
{
    private readonly Lock _gate = new();
    private readonly List<VendorCall> _calls = [];

    /// <summary>One call: its kind (<c>plan</c>, <c>assign</c>, <c>review</c>, <c>task</c>), its agent and its instant.</summary>
    public sealed record VendorCall(string Kind, string Agent, TimeSpan At);

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <summary>Awaited before a call of the kind it is given answers; null holds nothing.</summary>
    public Func<string, CancellationToken, Task>? Hold { get; set; }

    /// <summary>The calls so far, oldest first.</summary>
    public IReadOnlyList<VendorCall> Calls
    {
        get { lock (_gate) return [.. _calls]; }
    }

    /// <summary>The instants of the calls of <paramref name="kind"/> (any kind when null) made for <paramref name="agent"/> (anyone when null).</summary>
    public TimeSpan[] InstantsOf(string? agent = null, string? kind = null)
    {
        lock (_gate)
        {
            return [.. _calls
                .Where(call => (agent is null || call.Agent == agent) && (kind is null || call.Kind == kind))
                .Select(call => call.At)];
        }
    }

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        AnswerAsync(prompt, cancellationToken);

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        AnswerAsync(string.Join("\n", messages.Select(m => m.Content)), cancellationToken);

    private async Task<LlmResponse> AnswerAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = LlmUsageScope.Current;
        var kind = scope.Operation == LlmUsageOperations.Planning ? "plan"
            : prompt.Contains("responsible for assigning tasks", StringComparison.Ordinal) ? "assign"
            : prompt.Contains("reviewing the output of a completed task", StringComparison.Ordinal) ? "review"
            : "task";
        lock (_gate)
            _calls.Add(new VendorCall(kind, scope.AgentId, now()));

        if (Hold is not null)
            await Hold(kind, cancellationToken);

        return new LlmResponse
        {
            Content = kind switch
            {
                "plan" => """{"plans": [{"task": 1, "plan": "1. Do it."}, {"task": 2, "plan": "1. Do it."}]}""",
                "assign" => $$"""{"agent_id": "{{ListedAgentId().Match(prompt).Groups[1].Value}}", "reason": "listed first"}""",
                "review" => """{"approved": true, "feedback": ""}""",
                _ => "done",
            },
            PromptTokens = 10,
            CompletionTokens = 2,
            TokensUsed = 12,
        };
    }

    [GeneratedRegex(@"^- ID: (\S+)", RegexOptions.Multiline)]
    private static partial Regex ListedAgentId();
}
