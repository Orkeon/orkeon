using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Bundles the configuration captured by <c>crewBuilder()</c> and handed to the
/// <see cref="JsCrew"/> constructor (name, process, members, tasks, manager, budget,
/// verbosity, optional logger/LLM provider and goal). Introduced to keep the
/// <see cref="JsCrew"/> constructor below the parameter-count threshold while grouping
/// these cohesive crew-definition attributes.
/// </summary>
internal sealed record JsCrewDefinition
{
    public required string Name { get; init; }
    public required string Process { get; init; }
    public required IEnumerable<JsAgent> Agents { get; init; }
    public required IEnumerable<JsTask> Tasks { get; init; }
    public JsAgent? Manager { get; init; }
    public required IReadOnlyDictionary<string, object?> Budget { get; init; }
    public bool Verbose { get; init; }
    public ILogger? Logger { get; init; }
    public ILlmProvider? LlmProvider { get; init; }

    /// <summary>Built-in tool instances exposed to agents (for <c>ctx.llm.act</c> tool-calling).</summary>
    public IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? BuiltInTools { get; init; }

    /// <summary>Optional per-tool-call permission gate applied inside <c>ctx.llm.act</c>.</summary>
    public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }

    /// <summary>Optional host-native renderer for streamed <c>ctx.llm.act</c> deltas (F5 L3).</summary>
    public Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? DeltaSink { get; init; }

    /// <summary>Optional single tool-invocation point applied inside <c>ctx.llm.act</c> (GAP-09).</summary>
    public Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? ToolInvocation { get; init; }

    public string? Goal { get; init; }

    /// <summary>
    /// YAML parity <c>memory: true</c> — the crew remembers: each run stores the result of its tasks
    /// and recalls the closest ones before each task, in the host's default memory store, under the
    /// crew's name (GAP-30). Off by default. Not <c>ctx.memory.*</c>, the scoped key/value stores of a run.
    /// </summary>
    public bool Memory { get; init; }
}
