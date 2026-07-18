using System.Collections.ObjectModel;

namespace Orkeon.Infrastructure.Configuration;

// YAML deserialization models for crew definitions (single-file and multi-file formats).
// Extracted from YamlCrewDefinitionLoader (R4.2 god-file decomposition); same namespace,
// so no consumer using-directives change.

// CA2227 justified suppression: the YAML config DTOs below intentionally expose
// settable collection properties. The YamlDotNet deserializer assigns whole
// collection instances during materialization, which requires public setters.
// Replacing them with init-only / read-only collections would break deserialization.
// Scope is limited to these binding DTOs; restored at end of file.
#pragma warning disable CA2227 // Collection properties should be read only

/// <summary>
/// YAML model for agent configuration in Python Orkeon format.
/// </summary>
public class AgentYamlConfig
{
    /// <summary>Gets or sets the agent role.</summary>
    public string? Role { get; set; }
    /// <summary>Gets or sets the agent goal.</summary>
    public string? Goal { get; set; }
    /// <summary>Gets or sets the agent backstory.</summary>
    public string? Backstory { get; set; }
    /// <summary>Gets or sets the list of tool names.</summary>
    public Collection<string>? Tools { get; set; }
    /// <summary>Gets or sets whether the agent allows delegation.</summary>
    public bool? AllowDelegation { get; set; }
    /// <summary>Gets or sets the maximum iterations.</summary>
    public int? MaxIter { get; set; }
    /// <summary>Gets or sets the maximum requests per minute.</summary>
    public int? MaxRpm { get; set; }
    /// <summary>Gets or sets whether verbose mode is enabled.</summary>
    public bool? Verbose { get; set; }
    /// <summary>Gets or sets the LLM configuration.</summary>
    public LlmYamlConfig? Llm { get; set; }
    /// <summary>Gets or sets the guardrails configuration.</summary>
    public GuardrailsYamlConfig? Guardrails { get; set; }
}

/// <summary>
/// YAML model for agent guardrails configuration.
/// Supports presets, global rules, and tool-specific clauses.
/// </summary>
public class GuardrailsYamlConfig
{
    /// <summary>Gets or sets the preset name ("analysis", "strict", "creative").</summary>
    public string? Preset { get; set; }
    /// <summary>Gets or sets the optional custom header for the guardrails section.</summary>
    public string? Header { get; set; }
    /// <summary>Gets or sets global rules applied regardless of tools.</summary>
    public Collection<string>? Rules { get; set; }
    /// <summary>Gets or sets tool-specific rules. Key = tool name, value = list of rules.</summary>
    public Dictionary<string, List<string>>? ToolRules { get; set; }
}

/// <summary>
/// YAML model for task configuration in Python Orkeon format.
/// </summary>
public class TaskYamlConfig
{
    /// <summary>Gets or sets the task description.</summary>
    public string? Description { get; set; }
    /// <summary>Gets or sets the expected output.</summary>
    public string? ExpectedOutput { get; set; }
    /// <summary>Gets or sets the assigned agent identifier.</summary>
    public string? Agent { get; set; }
    /// <summary>Gets or sets the list of tool names scoped to this task (Python Orkeon parity).</summary>
    public Collection<string>? Tools { get; set; }
    /// <summary>Gets or sets the list of dependency task identifiers.</summary>
    public Collection<string>? Dependencies { get; set; }
    /// <summary>Gets or sets whether the task executes asynchronously.</summary>
    public bool? AsyncExecution { get; set; }
    /// <summary>Gets or sets whether human input is required.</summary>
    public bool? HumanInput { get; set; }
    /// <summary>Gets or sets additional context key-value pairs.</summary>
    public Dictionary<string, object>? Context { get; set; }
    /// <summary>Gets or sets the circuit breaker / FSM configuration for this task.</summary>
    public CircuitBreakerYamlConfig? CircuitBreaker { get; set; }
    /// <summary>Gets or sets the deliverable contract (framework-managed output file).</summary>
    public DeliverableYamlConfig? Deliverable { get; set; }
    /// <summary>Gets or sets the optional per-task LLM override (response_format, temperature, …).</summary>
    public LlmOverrideYamlConfig? LlmOverride { get; set; }
    /// <summary>Gets or sets the optional per-task guardrails (same shape as agent-level guardrails).</summary>
    public GuardrailsYamlConfig? Guardrails { get; set; }
}

/// <summary>
/// YAML model for the optional <c>llm_override:</c> sub-block on a task. Each field is
/// nullable; <c>null</c> means "leave the agent's value untouched". Mirrors
/// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride"/> field-by-field.
/// </summary>
public class LlmOverrideYamlConfig
{
    /// <summary>Output-format constraint forwarded to providers that implement it (<c>"text"</c> | <c>"json_object"</c>).</summary>
    public string? ResponseFormat { get; set; }
    /// <summary>Sampling temperature override.</summary>
    public double? Temperature { get; set; }
    /// <summary>Maximum output tokens override.</summary>
    public int? MaxTokens { get; set; }
    /// <summary>Nucleus sampling probability override.</summary>
    public double? TopP { get; set; }
    /// <summary>Thinking-mode toggle and reasoning-effort hint override.</summary>
    public ThinkingYamlConfig? Thinking { get; set; }
}

/// <summary>
/// YAML model for a task deliverable contract. When present, the framework takes
/// over the production of the output file instead of relying on the agent's
/// explicit <c>file_write</c> tool call.
/// </summary>
public class DeliverableYamlConfig
{
    /// <summary>Virtual path where the deliverable is written (e.g. <c>/output/report.md</c>).</summary>
    public string? Path { get; set; }
    /// <summary>
    /// Production mode: <c>final_message</c> (framework writes final assistant text),
    /// <c>structured_output</c> (grammar-constrained JSON), or <c>tool_call</c> (legacy).
    /// </summary>
    public string? Source { get; set; }
    /// <summary>Logical format hint (<c>markdown</c>, <c>json</c>, <c>text</c>). Default: <c>markdown</c>.</summary>
    public string? Format { get; set; }
    /// <summary>Whether to sanitize trailing template tokens / orphan triple-quotes. Default: true.</summary>
    public bool? Sanitize { get; set; }
    /// <summary>Virtual path to an external JSON Schema file (Structured Output only).</summary>
    public string? SchemaPath { get; set; }
    /// <summary>Inline JSON Schema string. Alternative to <see cref="SchemaPath"/>.</summary>
    public string? SchemaInline { get; set; }
}

/// <summary>
/// YAML model for circuit breaker / FSM configuration.
/// Usable at crew level (default for all tasks) or per-task (override).
/// </summary>
public class CircuitBreakerYamlConfig
{
    /// <summary>Gets or sets the preset name: "strict", "permissive", or "default".</summary>
    public string? Preset { get; set; }
    /// <summary>Gets or sets the maximum transitions before tripping.</summary>
    public int? MaxTransitions { get; set; }
    /// <summary>Gets or sets the state timeout in seconds.</summary>
    public int? StateTimeoutSeconds { get; set; }
    /// <summary>Gets or sets the max state visits (cycle detection).</summary>
    public int? MaxStateVisits { get; set; }
    /// <summary>Gets or sets the max total duration in seconds.</summary>
    public int? MaxTotalDurationSeconds { get; set; }
    /// <summary>Gets or sets whether to use degraded mode instead of throwing.</summary>
    public bool? UseDegradedMode { get; set; }
    /// <summary>Gets or sets the max retries after failure.</summary>
    public int? MaxRetries { get; set; }
    /// <summary>Gets or sets the max tool calls per execution round.</summary>
    public int? MaxToolCallsPerRound { get; set; }
    /// <summary>Gets or sets the max validation retries.</summary>
    public int? MaxValidationRetries { get; set; }
}

/// <summary>
/// YAML model for graph-specific configuration when process type is "graph".
/// Controls retry cycles and circuit breaker behavior for the state graph engine.
/// </summary>
public class GraphYamlConfig
{
    /// <summary>Gets or sets the maximum retry cycles for failed tasks (default: 2).</summary>
    public int? MaxRetryCycles { get; set; }
    /// <summary>Gets or sets the circuit breaker preset ("strict", "permissive", "default").</summary>
    public string? CircuitBreakerPreset { get; set; }
    /// <summary>Gets or sets the max transitions before tripping (overrides preset).</summary>
    public int? MaxTransitions { get; set; }
    /// <summary>Gets or sets the max state visits / cycle detection (overrides preset).</summary>
    public int? MaxStateVisits { get; set; }
    /// <summary>Gets or sets the max total duration in seconds (overrides preset).</summary>
    public int? MaxTotalDurationSeconds { get; set; }
}

/// <summary>
/// YAML model for LLM configuration.
/// </summary>
public class LlmYamlConfig
{
    /// <summary>Gets or sets the model identifier.</summary>
    public string? Model { get; set; }
    /// <summary>Gets or sets the sampling temperature.</summary>
    public double? Temperature { get; set; }
    /// <summary>Gets or sets the maximum token limit.</summary>
    public int? MaxTokens { get; set; }
    /// <summary>Gets or sets the nucleus sampling probability (DeepSeek recommends 1.0). Optional.</summary>
    public double? TopP { get; set; }
    /// <summary>Gets or sets the optional thinking-mode toggle and reasoning-effort hint (DeepSeek V4, …).</summary>
    public ThinkingYamlConfig? Thinking { get; set; }
    /// <summary>
    /// Gets or sets the output-format constraint forwarded to providers that implement
    /// <c>response_format</c> (e.g. DeepSeek). Accepted: <c>"text"</c>, <c>"json_object"</c>
    /// (case-insensitive). Unknown values are downgraded to <c>null</c> with a warning.
    /// </summary>
    public string? ResponseFormat { get; set; }
}

/// <summary>
/// YAML model for the optional thinking-mode block nested under <see cref="LlmYamlConfig.Thinking"/>.
/// All fields are optional — omitting one keeps the provider default in place.
/// </summary>
public class ThinkingYamlConfig
{
    /// <summary>Gets or sets whether thinking mode is enabled (default: provider-managed).</summary>
    public bool? Enabled { get; set; }
    /// <summary>Gets or sets the reasoning effort hint ("low" | "medium" | "high" | "max").</summary>
    public string? Effort { get; set; }
}

/// <summary>
/// YAML model for a single-file crew definition.
/// </summary>
public class CrewYamlConfig
{
    /// <summary>Gets or sets the crew name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the crew goal.</summary>
    public string? Goal { get; set; }
    /// <summary>Gets or sets the process type (sequential, hierarchical, etc.).</summary>
    public string? Process { get; set; }
    /// <summary>Gets or sets whether verbose mode is enabled.</summary>
    public bool? Verbose { get; set; }
    /// <summary>Gets or sets whether memory is enabled.</summary>
    public bool? Memory { get; set; }
    /// <summary>Gets or sets the memory provider name (e.g. "Redis", "Sqlite", "InMemory").</summary>
    public string? MemoryProvider { get; set; }
    /// <summary>Gets or sets whether planning is enabled.</summary>
    public bool? Planning { get; set; }
    /// <summary>Gets or sets the manager agent identifier for hierarchical process.</summary>
    public string? ManagerAgent { get; set; }
    /// <summary>Gets or sets the default circuit breaker / FSM configuration for all tasks.</summary>
    public CircuitBreakerYamlConfig? CircuitBreaker { get; set; }
    /// <summary>Gets or sets the graph-specific configuration (only used when process is "graph").</summary>
    public GraphYamlConfig? GraphConfig { get; set; }
    /// <summary>Gets or sets the crew-default LLM configuration applied to agents without their own (Python Orkeon parity).</summary>
    public LlmYamlConfig? Llm { get; set; }
    /// <summary>Gets or sets the agent configurations keyed by agent identifier.</summary>
    public Dictionary<string, AgentYamlConfig>? Agents { get; set; }
    /// <summary>Gets or sets the task configurations keyed by task identifier.</summary>
    public Dictionary<string, TaskYamlConfig>? Tasks { get; set; }
}

/// <summary>
/// YAML model for the crew.yaml file in multi-file format (contains only crew-level settings).
/// </summary>
public class CrewSettingsYamlConfig
{
    /// <summary>Gets or sets the crew name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the crew goal.</summary>
    public string? Goal { get; set; }
    /// <summary>Gets or sets the process type.</summary>
    public string? Process { get; set; }
    /// <summary>Gets or sets whether verbose mode is enabled.</summary>
    public bool? Verbose { get; set; }
    /// <summary>Gets or sets whether memory is enabled.</summary>
    public bool? Memory { get; set; }
    /// <summary>Gets or sets the memory provider name (e.g. "Redis", "Sqlite", "InMemory").</summary>
    public string? MemoryProvider { get; set; }
    /// <summary>Gets or sets whether planning is enabled.</summary>
    public bool? Planning { get; set; }
    /// <summary>Gets or sets the manager agent identifier.</summary>
    public string? ManagerAgent { get; set; }
    /// <summary>Gets or sets the default circuit breaker / FSM configuration for all tasks.</summary>
    public CircuitBreakerYamlConfig? CircuitBreaker { get; set; }
    /// <summary>Gets or sets the graph-specific configuration (only used when process is "graph").</summary>
    public GraphYamlConfig? GraphConfig { get; set; }
    /// <summary>Gets or sets the crew-default LLM configuration applied to agents without their own (Python Orkeon parity).</summary>
    public LlmYamlConfig? Llm { get; set; }
}

#pragma warning restore CA2227 // Collection properties should be read only
