using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Immutable snapshot of the persisted state of an <see cref="Agent"/> aggregate.
/// Used by <see cref="Agent.Restore(AgentSnapshot)"/> to rehydrate an agent from
/// persistence with named members instead of a long positional parameter list
/// (which is error-prone when adjacent parameters share a type).
/// </summary>
public sealed record AgentSnapshot
{
    /// <summary>The agent identifier.</summary>
    public required AgentId Id { get; init; }

    /// <summary>The agent role.</summary>
    public required AgentRole Role { get; init; }

    /// <summary>The agent goal.</summary>
    public required AgentGoal Goal { get; init; }

    /// <summary>The optional agent backstory.</summary>
    public AgentBackstory? Backstory { get; init; }

    /// <summary>Whether the agent may delegate work.</summary>
    public bool AllowDelegation { get; init; }

    /// <summary>The maximum number of reasoning iterations.</summary>
    public int MaxIterations { get; init; }

    /// <summary>The maximum requests-per-minute throttle.</summary>
    public int MaxRpm { get; init; }

    /// <summary>Whether verbose execution logging is enabled.</summary>
    public bool Verbose { get; init; }

    /// <summary>The persisted agent status.</summary>
    public required AgentStatus Status { get; init; }

    /// <summary>The optional maximum wall-clock execution time.</summary>
    public TimeSpan? MaxExecutionTime { get; init; }

    /// <summary>Whether result caching is enabled.</summary>
    public bool CacheEnabled { get; init; }

    /// <summary>The optional system prompt template.</summary>
    public string? SystemTemplate { get; init; }

    /// <summary>The optional prompt template.</summary>
    public string? PromptTemplate { get; init; }

    /// <summary>The optional response template.</summary>
    public string? ResponseTemplate { get; init; }

    /// <summary>The maximum retry limit.</summary>
    public int MaxRetryLimit { get; init; }

    /// <summary>The optional function-calling LLM provider.</summary>
    public ILlmProvider? FunctionCallingLlm { get; init; }

    /// <summary>The optional step callback.</summary>
    public IStepCallback? StepCallback { get; init; }

    /// <summary>The optional tool access policy (defaults to unrestricted).</summary>
    public ToolAccessPolicy? ToolAccessPolicy { get; init; }

    /// <summary>The tools assigned to the agent.</summary>
    public IEnumerable<ITool>? Tools { get; init; }

    /// <summary>The tasks assigned to the agent.</summary>
    public IEnumerable<TaskId>? AssignedTasks { get; init; }

    /// <summary>The persisted agent memories.</summary>
    public IEnumerable<AgentMemory>? Memories { get; init; }

    /// <summary>The task currently being executed, if any.</summary>
    public TaskId? CurrentTask { get; init; }

    /// <summary>The optional guardrails configuration.</summary>
    public GuardrailsConfig? Guardrails { get; init; }

    /// <summary>The knowledge (RAG) collections attached to the agent, if any.</summary>
    public IEnumerable<KnowledgeAttachment>? KnowledgeAttachments { get; init; }
}
