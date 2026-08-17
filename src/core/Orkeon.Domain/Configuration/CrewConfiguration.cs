using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Configuration;

/// <summary>Configuration for crew setup and execution.</summary>
public sealed record CrewConfiguration
{
    /// <summary>Gets the name of the crew.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the goal of the crew.</summary>
    public string Goal { get; init; } = string.Empty;
    /// <summary>Gets the agent configurations for this crew.</summary>
    public IReadOnlyList<AgentConfiguration> Agents { get; init; } = Array.Empty<AgentConfiguration>();
    /// <summary>Gets the task configurations for this crew.</summary>
    public IReadOnlyList<TaskConfiguration> Tasks { get; init; } = Array.Empty<TaskConfiguration>();
    /// <summary>Gets the process type for task execution.</summary>
    public ProcessType Process { get; init; } = ProcessType.Sequential;
    /// <summary>Gets a value indicating whether verbose logging is enabled.</summary>
    public bool Verbose { get; init; }
    /// <summary>Gets a value indicating whether memory is enabled.</summary>
    public bool Memory { get; init; }
    /// <summary>Gets the memory provider name (e.g. "Redis", "Sqlite", "InMemory"), or null for default.</summary>
    public string? MemoryProvider { get; init; }
    /// <summary>Gets a value indicating whether planning is enabled.</summary>
    public bool Planning { get; init; }
    /// <summary>Gets the identifier of the manager agent, or null for no manager.</summary>
    public AgentId? ManagerAgentId { get; init; }
    /// <summary>Gets the execution configuration override, or null to use defaults.</summary>
    public ExecutionConfig? ExecutionConfig { get; init; }
    /// <summary>Gets the default circuit breaker / FSM configuration for all tasks, or null for built-in defaults.</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }
    /// <summary>Gets the graph-specific configuration (only used when Process is Graph), or null for defaults.</summary>
    public GraphConfig? GraphConfig { get; init; }
    /// <summary>
    /// Gets the crew-level RAG configuration (<c>rag:</c> block — provider, declared collections
    /// with their ingestion sources, retrieval defaults), or null when the crew declares none.
    /// Parsing-only for now: kickoff-time ingestion consumes it in a later lot (RAG-03/C4).
    /// </summary>
    public RagCrewConfig? Rag { get; init; }
    /// <summary>Gets additional metadata for this crew configuration.</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>Configuration for agent setup.</summary>
public sealed record AgentConfiguration
{
    /// <summary>Gets the unique identifier of this agent configuration.</summary>
    public AgentId Id { get; init; } = AgentId.Create();
    /// <summary>Gets the role of the agent.</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>Gets the goal of the agent.</summary>
    public string Goal { get; init; } = string.Empty;
    /// <summary>Gets the backstory of the agent.</summary>
    public string Backstory { get; init; } = string.Empty;
    /// <summary>Gets the tool names available to this agent.</summary>
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();
    /// <summary>Gets a value indicating whether delegation is allowed.</summary>
    public bool AllowDelegation { get; init; } = true;
    /// <summary>Gets the maximum number of iterations per task.</summary>
    public int MaxIterations { get; init; } = 20;
    /// <summary>Gets the maximum requests per minute.</summary>
    public int MaxRPM { get; init; } = 10;
    /// <summary>Gets a value indicating whether verbose logging is enabled for this agent.</summary>
    public bool Verbose { get; init; }
    /// <summary>Gets the LLM configuration for this agent, or null to use defaults.</summary>
    public LlmConfig? LlmConfig { get; init; }
    /// <summary>Gets the system prompt template override.</summary>
    public string? SystemTemplate { get; init; }
    /// <summary>Gets the prompt template override.</summary>
    public string? PromptTemplate { get; init; }
    /// <summary>Gets the response template override.</summary>
    public string? ResponseTemplate { get; init; }
    /// <summary>Gets the guardrails configuration for this agent, or null for no guardrails.</summary>
    public GuardrailsConfig? Guardrails { get; init; }
    /// <summary>
    /// Gets the knowledge (RAG) collections attached to this agent (<c>knowledge:</c> block,
    /// short or long form). Empty when the agent declares none.
    /// </summary>
    public IReadOnlyList<KnowledgeAttachment> KnowledgeAttachments { get; init; } = Array.Empty<KnowledgeAttachment>();
}

/// <summary>Configuration for task setup.</summary>
public sealed record TaskConfiguration
{
    /// <summary>Gets the unique identifier of this task configuration.</summary>
    public TaskId Id { get; init; } = TaskId.Create();
    /// <summary>Gets the task description.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the expected output description.</summary>
    public string ExpectedOutput { get; init; } = string.Empty;
    /// <summary>Gets the identifier of the assigned agent, or null for auto-assignment.</summary>
    public AgentId? AssignedAgentId { get; init; }
    /// <summary>Gets the identifiers of tasks this task depends on.</summary>
    public IReadOnlyList<TaskId> Dependencies { get; init; } = Array.Empty<TaskId>();
    /// <summary>Gets the names of tools scoped to this task (snake_case YAML parity).</summary>
    public IReadOnlyList<string> RequiredTools { get; init; } = Array.Empty<string>();
    /// <summary>Gets additional context data for this task.</summary>
    public Dictionary<string, object> Context { get; init; } = [];
    /// <summary>Gets a value indicating whether this task should be executed asynchronously.</summary>
    public bool AsyncExecution { get; init; }
    /// <summary>Gets a value indicating whether human input is required.</summary>
    public bool HumanInput { get; init; }
    /// <summary>Gets the timeout in seconds, or null for no timeout.</summary>
    public int? TimeoutSeconds { get; init; }
    /// <summary>Gets the circuit breaker / FSM configuration for this task, or null for defaults.</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }
    /// <summary>
    /// Gets the framework-managed deliverable contract. When null, the task keeps legacy
    /// tool-call semantics (agent emits <c>file_write</c> itself).
    /// </summary>
    public Orkeon.Domain.Task.ValueObjects.TaskDeliverable? Deliverable { get; init; }

    /// <summary>
    /// Gets the per-task LLM override (e.g. <c>response_format: json_object</c>). Resolved
    /// over the agent's base config by <c>LlmConfigResolver</c>. <c>null</c> = no per-task patch.
    /// </summary>
    public Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride? LlmOverride { get; init; }

    /// <summary>
    /// Gets the optional per-task guardrails, injected into this task's prompt in addition to the
    /// assigned agent's guardrails (same <see cref="GuardrailsConfig"/> shape). <c>null</c> = none.
    /// </summary>
    public GuardrailsConfig? Guardrails { get; init; }
}
