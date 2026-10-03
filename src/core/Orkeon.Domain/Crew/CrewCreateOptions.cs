using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Options for creating a new crew. Groups all parameters for <see cref="Crew.Create(CrewCreateOptions)"/>.
/// </summary>
public sealed class CrewCreateOptions
{
    /// <summary>
    /// The goal of the crew (required).
    /// </summary>
    public string Goal { get; init; } = null!;

    /// <summary>
    /// Optional name of the crew — the <c>name:</c> of its configuration. It scopes the crew's
    /// long-term memory: the crews of one name share it from one run to the next. Null or blank
    /// leaves the crew unnamed, and its memory to the one run.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The process type for task execution.
    /// </summary>
    public ProcessType ProcessType { get; init; } = ProcessType.Sequential;

    /// <summary>
    /// Whether to enable verbose logging.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Whether to enable planning before execution.
    /// </summary>
    public bool Planning { get; init; }

    /// <summary>
    /// Maximum requests per minute.
    /// </summary>
    public int MaxRpm { get; init; } = CrewDefaults.DefaultMaxRpm;

    /// <summary>
    /// Whether to share the crew among agents.
    /// </summary>
    public bool ShareCrew { get; init; } = true;

    /// <summary>
    /// Optional output log file path.
    /// </summary>
    public string? OutputLogFile { get; init; }

    /// <summary>
    /// Optional provider the crew's manager runs on (C# <c>WithManagerLlm</c>): read by the
    /// Hierarchical and Autonomous processes only — <see cref="Crew.Create(CrewCreateOptions)"/>
    /// refuses it in the four others (GAP-33).
    /// </summary>
    public ILlmProvider? ManagerLlm { get; init; }

    /// <summary>
    /// Optional manager agent: the hierarchical manager, or the consensual crew's arbiter of the
    /// <c>ManagerDecision</c> fallback — <see cref="Crew.Create(CrewCreateOptions)"/> refuses it in
    /// the four other processes, which have none (GAP-33).
    /// </summary>
    public Common.AgentId? ManagerAgentId { get; init; }

    /// <summary>
    /// Language for the crew output.
    /// </summary>
    public string Language { get; init; } = CrewDefaults.DefaultLanguage;

    /// <summary>
    /// Whether to return full output from all tasks.
    /// </summary>
    public bool FullOutput { get; init; }

    /// <summary>
    /// Optional LLM provider the crew plans on, metered like a provider the host registers; null
    /// plans on the host's default LLM profile. Requires <see cref="Planning"/>:
    /// <see cref="Crew.Create(CrewCreateOptions)"/> refuses a planning provider for a crew that does
    /// not plan (GAP-33).
    /// </summary>
    public ILlmProvider? PlanningLlm { get; init; }

    /// <summary>
    /// Whether the crew remembers: only then does a run store the result of each task and recall
    /// the crew's memories before each task (GAP-30). Off by default.
    /// </summary>
    public bool MemoryEnabled { get; init; }

    /// <summary>
    /// Optional memory-provider selection (e.g. <c>inmemory</c>, <c>redis</c>, <c>sqlite</c>,
    /// <c>chromadb</c>, <c>pinecone</c>, <c>lancedb</c>). Null falls back to the host's configured
    /// default provider. Resolved to a concrete <c>IMemoryProvider</c> at kickoff. Requires
    /// <see cref="MemoryEnabled"/>: <see cref="Crew.Create(CrewCreateOptions)"/> refuses a provider
    /// for a crew without memory (GAP-30).
    /// </summary>
    public string? MemoryProvider { get; init; }

    /// <summary>
    /// Whether to allow dynamic agent creation during execution.
    /// </summary>
    public bool AllowDynamicAgents { get; init; }

    /// <summary>
    /// Maximum number of concurrent dynamic agents (null for unlimited).
    /// </summary>
    public int? MaxConcurrentDynamicAgents { get; init; }

    /// <summary>
    /// Optional tool access control policy override for all agents in this crew.
    /// When set, this policy overrides individual agent policies.
    /// </summary>
    public ToolAccessPolicy? ToolAccessPolicy { get; init; }

    /// <summary>
    /// Optional graph-orchestration configuration (retry cycles, circuit-breaker preset/limits).
    /// Only consumed when <see cref="ProcessType"/> is <c>Graph</c>.
    /// </summary>
    public GraphConfig? GraphConfig { get; init; }
}
