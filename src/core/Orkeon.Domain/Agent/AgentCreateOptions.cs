using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Options for creating a new agent. Groups all parameters for <see cref="Agent.Create(AgentCreateOptions)"/>.
/// </summary>
public sealed class AgentCreateOptions
{
    /// <summary>
    /// The role of the agent (required).
    /// </summary>
    public AgentRole Role { get; init; } = null!;

    /// <summary>
    /// The goal of the agent (required).
    /// </summary>
    public AgentGoal Goal { get; init; } = null!;

    /// <summary>
    /// Optional backstory for the agent.
    /// </summary>
    public AgentBackstory? Backstory { get; init; }

    /// <summary>
    /// Whether the agent is allowed to delegate tasks to other agents.
    /// </summary>
    public bool AllowDelegation { get; init; }

    /// <summary>
    /// Maximum number of iterations for the agent execution loop.
    /// </summary>
    public int MaxIterations { get; init; } = AgentDefaults.MaxIterations;

    /// <summary>
    /// Maximum requests per minute for rate-limiting agent LLM calls.
    /// </summary>
    public int MaxRpm { get; init; } = AgentDefaults.MaxRequestsPerMinute;

    /// <summary>
    /// Whether to enable verbose logging.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Maximum execution time for tasks.
    /// </summary>
    public TimeSpan? MaxExecutionTime { get; init; }

    /// <summary>
    /// Whether caching is enabled.
    /// </summary>
    public bool CacheEnabled { get; init; } = true;

    /// <summary>
    /// Optional system prompt template.
    /// </summary>
    public string? SystemTemplate { get; init; }

    /// <summary>
    /// Optional prompt template.
    /// </summary>
    public string? PromptTemplate { get; init; }

    /// <summary>
    /// Optional response template.
    /// </summary>
    public string? ResponseTemplate { get; init; }

    /// <summary>
    /// Maximum number of retry attempts on task execution failure.
    /// </summary>
    public int MaxRetryLimit { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>
    /// Optional LLM provider for function calling.
    /// </summary>
    public ILlmProvider? FunctionCallingLlm { get; init; }

    /// <summary>
    /// Optional collection of tools to assign to the agent.
    /// </summary>
    public IEnumerable<ITool>? Tools { get; init; }

    /// <summary>
    /// Optional knowledge (RAG) collections attached to the agent. Each attachment is
    /// validated on creation (<see cref="KnowledgeAttachment.Validate"/>). Empty/null = none.
    /// </summary>
    public IEnumerable<KnowledgeAttachment>? KnowledgeAttachments { get; init; }

    /// <summary>
    /// Optional step callback handler.
    /// </summary>
    public IStepCallback? StepCallback { get; init; }

    /// <summary>
    /// Optional tool access control policy for the agent.
    /// If not specified, defaults to unrestricted access.
    /// </summary>
    public ToolAccessPolicy? ToolAccessPolicy { get; init; }

    /// <summary>
    /// Optional guardrails configuration injected into the agent's system prompt.
    /// Use <see cref="GuardrailPresets"/> for built-in presets or <see cref="GuardrailsBuilder"/> for custom rules.
    /// </summary>
    public GuardrailsConfig? Guardrails { get; init; }

    /// <summary>
    /// Optional per-agent LLM configuration that overrides the crew-level default on a
    /// field-by-field basis (Experiment 07 friction #7). When set, the runtime applies its
    /// Model / Temperature / MaxTokens / TopP / Thinking to the agent's chat requests so
    /// hierarchical crews can pick e.g. <c>deepseek-v4-pro</c> for the planner and
    /// <c>deepseek-v4-flash</c> for classification agents within the same crew.
    /// </summary>
    public LlmConfig? LlmConfig { get; init; }
}
