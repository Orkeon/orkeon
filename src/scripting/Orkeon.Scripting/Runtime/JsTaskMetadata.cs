using Jint.Native;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Bundles the DSL-only metadata attached to a <see cref="JsTask"/> by
/// <c>taskBuilder()</c> (assigned agent, dependency tasks, JSON-schema expectations,
/// task-level tools, deliverable spec, response format and LLM profile). Introduced to keep the
/// <see cref="JsTask"/> constructor below the parameter-count threshold while grouping
/// these cohesive, mostly-optional attributes.
/// </summary>
internal sealed record JsTaskMetadata
{
    public JsAgent? AssignedAgent { get; init; }
    public IReadOnlyList<JsTask> Context { get; init; } = [];
    public JsValue? ExpectSchema { get; init; }
    public JsValue? DeliverableSpec { get; init; }
    public string? ResponseFormat { get; init; }

    /// <summary>Schema name captured by <c>taskBuilder().withResponseSchema(...)</c>.</summary>
    public string? ResponseSchemaName { get; init; }

    /// <summary>Schema document captured by <c>withResponseSchema</c>, serialized by the adapter.</summary>
    public JsValue? ResponseSchema { get; init; }

    /// <summary>Whether the captured schema is strict. Defaults to true.</summary>
    public bool ResponseSchemaStrict { get; init; } = true;

    /// <summary>The host LLM profile captured by <c>taskBuilder().withProfile(name)</c> (GAP-19).</summary>
    public string? Profile { get; init; }

    /// <summary>YAML parity <c>humanInput: true</c> — the task pauses for the human-input provider.</summary>
    public bool HumanInput { get; init; }

    /// <summary>YAML parity <c>asyncExecution: true</c> — a sequential crew runs the task alongside the tasks after it (GAP-22).</summary>
    public bool AsyncExecution { get; init; }

    /// <summary>Task-level <c>tools:</c> — names or <see cref="JsTool"/> instances the task requires.</summary>
    public IReadOnlyList<JsValue> Tools { get; init; } = [];
}
