namespace Orkeon.Domain.Common;

// -- Core Aggregates --------------------------------------------------------
/// <summary>Strongly-typed identifier for Agent entities.</summary>
public sealed class AgentId : EntityId<AgentId> { }
/// <summary>Strongly-typed identifier for AgentStep entities.</summary>
public sealed class AgentStepId : EntityId<AgentStepId> { }
/// <summary>Strongly-typed identifier for Task entities.</summary>
public sealed class TaskId : EntityId<TaskId> { }
/// <summary>Strongly-typed identifier for Crew entities.</summary>
public sealed class CrewId : EntityId<CrewId>
{
    /// <summary>
    /// Reserved sentinel <see cref="CrewId"/> representing the EventHub itself as a message source.
    /// Distinct from any user-created crew (ULID prefix <c>00…</c> never produced by <see cref="EntityId{TEntityId}.Create"/>
    /// since <see cref="Ulid.NewUlid()"/> stamps the current timestamp into the first 6 bytes).
    /// See <c>docs/architecture/event-hub-and-crew-lifecycle.md</c> §19.
    /// </summary>
    public static CrewId System { get; } = Parse("002RNK1NG000RKE0NSYSTEM000");

    /// <summary>Returns <see langword="true"/> when <paramref name="id"/> is the reserved <see cref="System"/> identifier.</summary>
    public static bool IsSystem(CrewId? id) => id is not null && id.Equals(System);
}
/// <summary>Strongly-typed identifier for Tool entities.</summary>
public sealed class ToolId : EntityId<ToolId> { }
/// <summary>Strongly-typed identifier for Memory entities.</summary>
public sealed class MemoryId : EntityId<MemoryId> { }
/// <summary>Strongly-typed identifier for Process entities.</summary>
public sealed class ProcessId : EntityId<ProcessId> { }
/// <summary>Strongly-typed identifier for KnowledgeSource entities.</summary>
public sealed class KnowledgeSourceId : EntityId<KnowledgeSourceId> { }
/// <summary>Strongly-typed identifier for Collaboration entities.</summary>
public sealed class CollaborationId : EntityId<CollaborationId> { }

// -- Memory -----------------------------------------------------------------
/// <summary>Strongly-typed identifier for MemoryStore entities.</summary>
public sealed class MemoryStoreId : EntityId<MemoryStoreId> { }
/// <summary>Strongly-typed identifier for MemoryItem entities.</summary>
public sealed class MemoryItemId : EntityId<MemoryItemId> { }
/// <summary>Strongly-typed identifier for Episode entities.</summary>
public sealed class EpisodeId : EntityId<EpisodeId> { }

// -- Delegation -------------------------------------------------------------
/// <summary>Strongly-typed identifier for DelegationRequest entities.</summary>
public sealed class DelegationRequestId : EntityId<DelegationRequestId> { }
/// <summary>Strongly-typed identifier for QuestionRequest entities.</summary>
public sealed class QuestionRequestId : EntityId<QuestionRequestId> { }

// -- HumanInput -------------------------------------------------------------
/// <summary>Strongly-typed identifier for HumanInputRequest entities.</summary>
public sealed class HumanInputRequestId : EntityId<HumanInputRequestId> { }

// -- Flows ------------------------------------------------------------------
/// <summary>Strongly-typed identifier for Flow entities.</summary>
public sealed class FlowId : EntityId<FlowId> { }
/// <summary>Strongly-typed identifier for FlowStep entities.</summary>
public sealed class FlowStepId : EntityId<FlowStepId> { }
/// <summary>Strongly-typed identifier for FlowEvent entities.</summary>
public sealed class FlowEventId : EntityId<FlowEventId> { }

// -- Planning ---------------------------------------------------------------
/// <summary>Strongly-typed identifier for TaskPlan entities.</summary>
public sealed class TaskPlanId : EntityId<TaskPlanId> { }
/// <summary>Strongly-typed identifier for PlanStep entities.</summary>
public sealed class PlanStepId : EntityId<PlanStepId> { }

// -- Composition ------------------------------------------------------------
/// <summary>Strongly-typed identifier for TrainingScenario entities.</summary>
public sealed class TrainingScenarioId : EntityId<TrainingScenarioId> { }
/// <summary>Strongly-typed identifier for TrainingObjective entities.</summary>
public sealed class TrainingObjectiveId : EntityId<TrainingObjectiveId> { }
/// <summary>Strongly-typed identifier for TrainingStep entities.</summary>
public sealed class TrainingStepId : EntityId<TrainingStepId> { }
/// <summary>Strongly-typed identifier for TrainingTask entities.</summary>
public sealed class TrainingTaskId : EntityId<TrainingTaskId> { }
/// <summary>Strongly-typed identifier for CrewTemplate entities.</summary>
public sealed class CrewTemplateId : EntityId<CrewTemplateId> { }

// -- Configuration ----------------------------------------------------------
/// <summary>Strongly-typed identifier for ConfigurationVersion entities.</summary>
public sealed class ConfigurationVersionId : EntityId<ConfigurationVersionId> { }

// -- Templates --------------------------------------------------------------
/// <summary>Strongly-typed identifier for AgentTemplate entities.</summary>
public sealed class AgentTemplateId : EntityId<AgentTemplateId> { }
/// <summary>Strongly-typed identifier for TaskTemplate entities.</summary>
public sealed class TaskTemplateId : EntityId<TaskTemplateId> { }

// -- Tools ------------------------------------------------------------------
/// <summary>Strongly-typed identifier for ToolCallRequest entities.</summary>
public sealed class ToolCallRequestId : EntityId<ToolCallRequestId> { }

// -- Knowledge --------------------------------------------------------------
/// <summary>Strongly-typed identifier for KnowledgeContent entities.</summary>
public sealed class KnowledgeContentId : EntityId<KnowledgeContentId> { }

// -- Domain Events ----------------------------------------------------------
/// <summary>Strongly-typed identifier for DomainEvent entities.</summary>
public sealed class DomainEventId : EntityId<DomainEventId> { }
