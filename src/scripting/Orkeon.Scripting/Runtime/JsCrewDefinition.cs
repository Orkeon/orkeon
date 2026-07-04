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
    public required IEnumerable<object> Tasks { get; init; }
    public JsAgent? Manager { get; init; }
    public required IReadOnlyDictionary<string, object?> Budget { get; init; }
    public bool Verbose { get; init; }
    public ILogger? Logger { get; init; }
    public ILlmProvider? LlmProvider { get; init; }

    /// <summary>Built-in tool instances exposed to agents (for <c>ctx.llm.act</c> tool-calling).</summary>
    public IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? BuiltInTools { get; init; }

    /// <summary>Optional per-tool-call permission gate applied inside <c>ctx.llm.act</c>.</summary>
    public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }

    public string? Goal { get; init; }
}
