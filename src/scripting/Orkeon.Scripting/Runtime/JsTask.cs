using Jint.Native;
using Orkeon.Scripting.Builders;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Wrapper exposed to JS for a task built by <see cref="JsTaskBuilder"/>. Holds the
/// underlying <c>Orkeon.Domain.Task.CrewTask</c> aggregate plus DSL-only metadata
/// (assigned agent, JSON-schema expectations, task-scoped tool, dependency tasks).
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsTask
{
    public string name { get; }
    public string id { get; }
    public string description { get; }
    public string expectedOutput { get; }

    internal DomainCrewTask Domain { get; }
    internal JsAgent? AssignedAgent { get; }
    internal IReadOnlyList<JsTask> Context { get; }
    internal JsValue? ExpectSchema { get; }
    internal JsValue? TaskTool { get; }
    /// <summary>Raw object captured by <c>taskBuilder().deliverable({...})</c>; parsed
    /// into a domain <c>TaskDeliverable</c> by <c>JsCrewConfigurationAdapter</c>.</summary>
    internal JsValue? DeliverableSpec { get; }

    /// <summary>Captured value from <c>taskBuilder().withResponseFormat("json_object")</c>; <c>null</c> when unset.</summary>
    internal string? ResponseFormatValue { get; }

    /// <summary>Schema name captured by <c>taskBuilder().withResponseSchema(...)</c>; <c>null</c> when unset.</summary>
    internal string? ResponseSchemaName { get; }

    /// <summary>Schema document captured by <c>withResponseSchema</c>; <c>null</c> when unset.</summary>
    internal JsValue? ResponseSchema { get; }

    /// <summary>Whether the captured schema is strict.</summary>
    internal bool ResponseSchemaStrict { get; }

    internal JsTask(
        string name,
        DomainCrewTask domain,
        string description,
        string expectedOutput,
        JsTaskMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        this.name = name;
        id = domain.Id.Value.ToString();
        this.description = description;
        this.expectedOutput = expectedOutput;
        Domain = domain;
        AssignedAgent = metadata.AssignedAgent;
        Context = metadata.Context;
        ExpectSchema = metadata.ExpectSchema;
        TaskTool = metadata.TaskTool;
        DeliverableSpec = metadata.DeliverableSpec;
        ResponseFormatValue = metadata.ResponseFormat;
        ResponseSchemaName = metadata.ResponseSchemaName;
        ResponseSchema = metadata.ResponseSchema;
        ResponseSchemaStrict = metadata.ResponseSchemaStrict;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
