using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;
using DomainTaskDescription = Orkeon.Domain.Task.ValueObjects.TaskDescription;
using DomainExpectedOutput = Orkeon.Domain.Task.ValueObjects.ExpectedOutput;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Fluent builder exposed to JS as <c>taskBuilder()</c>. Captures the configuration
/// supplied by the script and produces a <see cref="JsTask"/> on <see cref="build"/>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsTaskBuilder
{
    private string? _name;
    private string? _description;
    private JsAgent? _agent;
    private string? _expectedOutput;
    private readonly List<JsTask> _context = new();
    private JsValue? _expectSchema;
    private JsValue? _taskTool;
    private JsValue? _deliverableSpec;
    private string? _responseFormat;
    private string? _responseSchemaName;
    private JsValue? _responseSchema;
    private bool _responseSchemaStrict = true;

    public JsTaskBuilder name(string value) { _name = value; return this; }
    public JsTaskBuilder description(string value) { _description = value; return this; }

    public JsTaskBuilder agent(JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        return this;
    }

    public JsTaskBuilder expectedOutput(string value) { _expectedOutput = value; return this; }

    public JsTaskBuilder withContext(JsTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _context.Add(task);
        return this;
    }

    public JsTaskBuilder withContexts(JsValue tasks)
    {
        if (tasks is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
            {
                var raw = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToObject();
                if (raw is JsTask t) _context.Add(t);
            }
        }
        return this;
    }

    public JsTaskBuilder expect(JsValue schema) { _expectSchema = schema; return this; }
    public JsTaskBuilder withTaskTool(JsValue tool) { _taskTool = tool; return this; }
    public JsTaskBuilder when(JsValue predicate) { _ = predicate; return this; }

    /// <summary>
    /// Forces the LLM output format on this task only (e.g. <c>"json_object"</c>).
    /// Stored as a string; the adapter materialises it into a
    /// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride"/> on the
    /// resulting <c>TaskConfiguration</c>. Pass <c>"text"</c> to keep the provider default.
    /// </summary>
    public JsTaskBuilder withResponseFormat(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidScriptException(".withResponseFormat(type) requires a non-empty string.");
        _responseFormat = type;
        return this;
    }

    /// <summary>Exposes the captured response_format value to the adapter; <c>null</c> when unset.</summary>
    internal string? ResponseFormatValue => _responseFormat;

    /// <summary>
    /// Constrains this task's output to a JSON Schema, on the providers whose API validates
    /// one server-side. Implies <c>response_format: json_schema</c>.
    /// </summary>
    /// <param name="name">Schema name, required by the OpenAI dialect.</param>
    /// <param name="schema">The JSON Schema, as an object literal or a JSON string.</param>
    /// <param name="strict">Whether the provider must reject any deviation. Defaults to true.</param>
    public JsTaskBuilder withResponseSchema(string name, JsValue schema, JsValue? strict = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidScriptException(".withResponseSchema(name, schema) requires a non-empty name.");
        if (schema is null || schema.IsUndefined() || schema.IsNull())
            throw new InvalidScriptException(".withResponseSchema(name, schema) requires a schema object or JSON string.");

        _responseSchemaName = name;
        _responseSchema = schema;
        _responseSchemaStrict = strict is null || strict.IsUndefined() || strict.IsNull() || strict.AsBoolean();
        return this;
    }

    /// <summary>
    /// First-class deliverable contract — mirrors YAML's <c>deliverable: { ... }</c>
    /// block. Expected shape:
    /// <code>{ path: string, source: 'final_message' | 'structured_output' | 'tool_call' | 'none',
    ///         format?: 'markdown' | 'json' | 'text', sanitize?: boolean,
    ///         schemaPath?: string, schema?: object, schemaInline?: string }</code>
    /// The <c>schema</c> field (inline object) is JSON-serialized into
    /// <c>schemaInline</c> by the adapter — both names are accepted, <c>schema</c> wins.
    /// </summary>
    public JsTaskBuilder deliverable(JsValue spec)
    {
        if (spec is null || spec.IsUndefined() || spec.IsNull())
            throw new InvalidScriptException(".deliverable(spec) requires a non-null spec object.");
        if (!spec.IsObject())
            throw new InvalidScriptException(".deliverable(spec) expects an object literal.");
        _deliverableSpec = spec;
        return this;
    }

    public JsTask build()
    {
        if (string.IsNullOrWhiteSpace(_description))
            throw new InvalidScriptException("taskBuilder() requires .description(...).");
        if (_agent is null)
            throw new InvalidScriptException("taskBuilder() requires .agent(...).");
        if (string.IsNullOrWhiteSpace(_expectedOutput))
            throw new InvalidScriptException("taskBuilder() requires .expectedOutput(...).");

        var domain = DomainCrewTask.Create(
            DomainTaskDescription.From(_description!),
            DomainExpectedOutput.From(_expectedOutput!));

        var displayName = string.IsNullOrWhiteSpace(_name) ? _description!.TruncateForName() : _name!;
        return new JsTask(displayName, domain, _description!, _expectedOutput!, new JsTaskMetadata
        {
            AssignedAgent = _agent,
            Context = _context,
            ExpectSchema = _expectSchema,
            TaskTool = _taskTool,
            DeliverableSpec = _deliverableSpec,
            ResponseFormat = _responseFormat,
            ResponseSchemaName = _responseSchemaName,
            ResponseSchema = _responseSchema,
            ResponseSchemaStrict = _responseSchemaStrict,
        });
    }
}

internal static class TaskBuilderStringHelpers
{
    public static string TruncateForName(this string source, int max = 60)
        => source.Length <= max ? source : source[..max];
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
