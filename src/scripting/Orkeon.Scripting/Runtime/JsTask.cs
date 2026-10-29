using Jint.Native;
using Orkeon.Scripting.Builders;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Wrapper exposed to JS for a task built by <see cref="JsTaskBuilder"/>. Holds the
/// underlying <c>Orkeon.Domain.Task.CrewTask</c> aggregate plus DSL-only metadata
/// (assigned agent, JSON-schema expectations, task-level tools, dependency tasks).
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of Task in Typings/task.d.ts; that declaration is the contract scripts read.
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

    /// <summary>The host LLM profile captured by <c>taskBuilder().withProfile(name)</c>; <c>null</c> when unset (GAP-19).</summary>
    internal string? ProfileValue { get; }

    /// <summary>YAML parity <c>humanInput</c> flag captured by <c>taskBuilder().humanInput(...)</c>.</summary>
    internal bool HumanInputFlag { get; }

    /// <summary>YAML parity <c>asyncExecution</c> flag captured by <c>taskBuilder().asyncExecution(...)</c>.</summary>
    internal bool AsyncExecutionFlag { get; }

    /// <summary>Task-level tools (names or <see cref="JsTool"/> instances) captured by <c>taskBuilder().tools(...)</c>.</summary>
    internal IReadOnlyList<JsValue> Tools { get; }

    /// <summary>The name given by <c>.name()</c>, or null.</summary>
    internal string? Key { get; }

    /// <summary>Plain data of <c>.decision({...})</c>, or null.</summary>
    internal object? DecisionSpec { get; }

    /// <summary>The decision task of <c>.when(...)</c>, or null.</summary>
    internal string? WhenTask { get; }

    /// <summary>The <c>.when</c> clauses as plain data, or the string <c>otherwise</c>.</summary>
    internal object? WhenSpec { get; }

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
        DeliverableSpec = metadata.DeliverableSpec;
        ResponseFormatValue = metadata.ResponseFormat;
        ResponseSchemaName = metadata.ResponseSchemaName;
        ResponseSchema = metadata.ResponseSchema;
        ResponseSchemaStrict = metadata.ResponseSchemaStrict;
        ProfileValue = metadata.Profile;
        HumanInputFlag = metadata.HumanInput;
        AsyncExecutionFlag = metadata.AsyncExecution;
        Tools = metadata.Tools;
        Key = metadata.Key;
        DecisionSpec = metadata.DecisionSpec;
        WhenTask = metadata.WhenTask;
        WhenSpec = metadata.WhenSpec;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
