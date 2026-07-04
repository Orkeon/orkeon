using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
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
    /// Optional LLM provider for the manager agent.
    /// </summary>
    public ILlmProvider? ManagerLlm { get; init; }

    /// <summary>
    /// Optional manager agent ID for hierarchical process.
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
    /// Optional step callback handler.
    /// </summary>
    public IStepCallback? StepCallback { get; init; }

    /// <summary>
    /// Optional task callback handler.
    /// </summary>
    public ITaskCallback? TaskCallback { get; init; }

    /// <summary>
    /// Optional LLM provider for planning.
    /// </summary>
    public ILlmProvider? PlanningLlm { get; init; }

    /// <summary>
    /// Whether to enable memory for the crew.
    /// </summary>
    public bool MemoryEnabled { get; init; }

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
}
