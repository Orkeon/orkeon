using Jint.Native;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Bundles the DSL-only metadata attached to a <see cref="JsTask"/> by
/// <c>taskBuilder()</c> (assigned agent, dependency tasks, JSON-schema expectations,
/// task-scoped tool, deliverable spec and response format). Introduced to keep the
/// <see cref="JsTask"/> constructor below the parameter-count threshold while grouping
/// these cohesive, mostly-optional attributes.
/// </summary>
internal sealed record JsTaskMetadata
{
    public JsAgent? AssignedAgent { get; init; }
    public IReadOnlyList<JsTask> Context { get; init; } = [];
    public JsValue? ExpectSchema { get; init; }
    public JsValue? TaskTool { get; init; }
    public JsValue? DeliverableSpec { get; init; }
    public string? ResponseFormat { get; init; }
}
