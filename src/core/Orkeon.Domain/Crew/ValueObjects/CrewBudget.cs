using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// What one run of an autonomous crew may spend (#218): YAML <c>budget:</c>, <c>crewBuilder().budget(...)</c>
/// in a script, C# <c>CrewBuilder.Budget</c>. Each run gets a fresh <see cref="AgentExecutionBudget"/> of
/// these limits, which its manager, its delegated agents and the agents they spawn spend together. A
/// limit left null bounds nothing an ordinary crew would reach: no token cap and no wall time, and the
/// structural limits <see cref="DefaultToolCalls"/>, <see cref="DefaultDelegationDepth"/> and
/// <see cref="DefaultSpawnedAgents"/>. Every limit set is 1 or more.
/// </summary>
public sealed record CrewBudget : ValueObjectRecord
{
    /// <summary>The tool calls a run may make when <see cref="ToolCalls"/> is not set.</summary>
    public const int DefaultToolCalls = 50;

    /// <summary>The delegation depth a run may reach when <see cref="DelegationDepth"/> is not set.</summary>
    public const int DefaultDelegationDepth = 4;

    /// <summary>The agents a run may spawn when <see cref="SpawnedAgents"/> is not set.</summary>
    public const int DefaultSpawnedAgents = 10;

    /// <summary>The budget of a crew that sets none: every limit at its default.</summary>
    public static CrewBudget Unset { get; } = new();

    /// <summary>The running sum of tokens (prompts plus completions) a run may spend; null: no cap.</summary>
    public int? Tokens { get; init; }

    /// <summary>The wall-clock time a run may last; null: no limit.</summary>
    public TimeSpan? WallTime { get; init; }

    /// <summary>The tool calls a run may make; null: <see cref="DefaultToolCalls"/>.</summary>
    public int? ToolCalls { get; init; }

    /// <summary>How deep delegation may go; null: <see cref="DefaultDelegationDepth"/>.</summary>
    public int? DelegationDepth { get; init; }

    /// <summary>The agents a run may spawn; null: <see cref="DefaultSpawnedAgents"/>.</summary>
    public int? SpawnedAgents { get; init; }

    /// <summary>A fresh budget of these limits for one run.</summary>
    [Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
    public AgentExecutionBudget ToExecutionBudget() => new()
    {
        MaxTokensConsumed = Tokens ?? int.MaxValue,
        MaxWallTime = WallTime ?? TimeSpan.MaxValue,
        MaxToolCalls = ToolCalls ?? DefaultToolCalls,
        MaxDelegationDepth = DelegationDepth ?? DefaultDelegationDepth,
        MaxSpawnedAgents = SpawnedAgents ?? DefaultSpawnedAgents,
    };

    /// <summary>The first limit set to zero or less, as <c>budget.&lt;name&gt;: value</c>; null when every limit set is valid.</summary>
    internal string? FirstInvalidLimit() =>
        Tokens is <= 0 ? $"budget.tokens: {Tokens}"
        : WallTime is { } wallTime && wallTime <= TimeSpan.Zero ? $"budget.wallTime: {wallTime:g}"
        : ToolCalls is <= 0 ? $"budget.toolCalls: {ToolCalls}"
        : DelegationDepth is <= 0 ? $"budget.delegationDepth: {DelegationDepth}"
        : SpawnedAgents is <= 0 ? $"budget.spawnedAgents: {SpawnedAgents}"
        : null;
}
