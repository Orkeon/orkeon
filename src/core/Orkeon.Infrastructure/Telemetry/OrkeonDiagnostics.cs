using System.Diagnostics;

namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// Central diagnostics registry for Orkeon OpenTelemetry instrumentation.
/// Provides ActivitySources for distributed tracing and standardized tag names.
/// </summary>
public static class OrkeonDiagnostics
{
    /// <summary>
    /// Service name used for OpenTelemetry resource identification.
    /// </summary>
    public const string ServiceName = "Orkeon";

    /// <summary>
    /// Service version used for OpenTelemetry resource identification.
    /// </summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// ActivitySource for crew-level operations (kickoff, batch, streaming).
    /// </summary>
    public static readonly ActivitySource CrewSource = new("Orkeon.Crew", ServiceVersion);

    /// <summary>
    /// ActivitySource for agent-level operations (execution, delegation).
    /// </summary>
    public static readonly ActivitySource AgentSource = new("Orkeon.Agent", ServiceVersion);

    /// <summary>
    /// ActivitySource for task-level operations (execution, planning, validation).
    /// </summary>
    public static readonly ActivitySource TaskSource = new("Orkeon.Task", ServiceVersion);

    /// <summary>
    /// ActivitySource for LLM provider operations (chat, generate, streaming).
    /// </summary>
    public static readonly ActivitySource LlmSource = new("Orkeon.Llm", ServiceVersion);

    /// <summary>
    /// ActivitySource for tool execution operations.
    /// </summary>
    public static readonly ActivitySource ToolSource = new("Orkeon.Tool", ServiceVersion);

    /// <summary>
    /// ActivitySource for memory operations (store, search, delete).
    /// </summary>
    public static readonly ActivitySource MemorySource = new("Orkeon.Memory", ServiceVersion);

    /// <summary>
    /// ActivitySource for EventHub messaging (publish, post, send, receive) — HUB-02.
    /// </summary>
    public static readonly ActivitySource EventHubSource = new("Orkeon.EventHub", ServiceVersion);

    /// <summary>
    /// All ActivitySource names for registration with OpenTelemetry.
    /// </summary>
    public static readonly string[] AllSourceNames =
    [
        "Orkeon.Crew",
        "Orkeon.Agent",
        "Orkeon.Task",
        "Orkeon.Llm",
        "Orkeon.Tool",
        "Orkeon.Memory",
        "Orkeon.EventHub"
    ];
}

/// <summary>
/// Standardized tag names for OpenTelemetry spans and metrics.
/// </summary>
public static class OrkeonDiagnosticTags
{
        // Crew tags
        /// <summary>Gets the tag name for crew ID.</summary>
        public const string CrewId = "orkeon.crew.id";
        /// <summary>Gets the tag name for crew name.</summary>
        public const string CrewName = "orkeon.crew.name";
        /// <summary>Gets the tag name for crew process type.</summary>
        public const string CrewProcess = "orkeon.crew.process";
        /// <summary>Gets the tag name for crew task count.</summary>
        public const string CrewTaskCount = "orkeon.crew.task_count";
        /// <summary>Gets the tag name for crew agent count.</summary>
        public const string CrewAgentCount = "orkeon.crew.agent_count";

        // Agent tags
        /// <summary>Gets the tag name for agent ID.</summary>
        public const string AgentId = "orkeon.agent.id";
        /// <summary>Gets the tag name for agent role.</summary>
        public const string AgentRole = "orkeon.agent.role";
        /// <summary>Gets the tag name for agent type.</summary>
        public const string AgentType = "orkeon.agent.type";
        /// <summary>Gets the tag name for agent iteration count.</summary>
        public const string AgentIteration = "orkeon.agent.iteration";

        // Task tags
        /// <summary>Gets the tag name for task ID.</summary>
        public const string TaskId = "orkeon.task.id";
        /// <summary>Gets the tag name for task name.</summary>
        public const string TaskName = "orkeon.task.name";
        /// <summary>Gets the tag name for task status.</summary>
        public const string TaskStatus = "orkeon.task.status";
        /// <summary>Gets the tag name for task success flag.</summary>
        public const string TaskSuccess = "orkeon.task.success";

        // LLM tags
        /// <summary>Gets the tag name for LLM provider.</summary>
        public const string LlmProvider = "orkeon.llm.provider";
        /// <summary>Gets the tag name for LLM model.</summary>
        public const string LlmModel = "orkeon.llm.model";
        /// <summary>Gets the tag name for LLM prompt token count.</summary>
        public const string LlmPromptTokens = "orkeon.llm.prompt_tokens";
        /// <summary>Gets the tag name for LLM completion token count.</summary>
        public const string LlmCompletionTokens = "orkeon.llm.completion_tokens";
        /// <summary>Gets the tag name for LLM total token count.</summary>
        public const string LlmTotalTokens = "orkeon.llm.total_tokens";
        /// <summary>Gets the tag name for LLM estimated cost in USD.</summary>
        public const string LlmCost = "orkeon.llm.cost_usd";
        /// <summary>Gets the tag name for LLM call success flag.</summary>
        public const string LlmSuccess = "orkeon.llm.success";

        // Tool tags
        /// <summary>Gets the tag name for tool name.</summary>
        public const string ToolName = "orkeon.tool.name";
        /// <summary>Gets the tag name for tool category.</summary>
        public const string ToolCategory = "orkeon.tool.category";
        /// <summary>Gets the tag name for tool execution success flag.</summary>
        public const string ToolSuccess = "orkeon.tool.success";
        /// <summary>Gets the tag name for tool error message.</summary>
        public const string ToolError = "orkeon.tool.error";

        // Memory tags
        /// <summary>Gets the tag name for memory operation type.</summary>
        public const string MemoryOperation = "orkeon.memory.operation";
        /// <summary>Gets the tag name for memory provider name.</summary>
        public const string MemoryProvider = "orkeon.memory.provider";
        /// <summary>Gets the tag name for memory item count.</summary>
        public const string MemoryItemCount = "orkeon.memory.item_count";

        // Security tags
        /// <summary>Gets the tag name for security event type.</summary>
        public const string SecurityEventType = "orkeon.security.event_type";

        // General tags
        /// <summary>Gets the tag name for error type.</summary>
        public const string ErrorType = "orkeon.error.type";
        /// <summary>Gets the tag name for error message.</summary>
        public const string ErrorMessage = "orkeon.error.message";
}
