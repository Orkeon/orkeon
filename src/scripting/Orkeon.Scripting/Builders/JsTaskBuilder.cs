using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;
using DomainTaskDescription = Orkeon.Domain.Task.ValueObjects.TaskDescription;
using DomainExpectedOutput = Orkeon.Domain.Task.ValueObjects.ExpectedOutput;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Fluent builder exposed to JS as <c>taskBuilder()</c>. Captures the configuration
/// supplied by the script and produces a <see cref="JsTask"/> on <see cref="build"/>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of TaskBuilder in Typings/task.d.ts; that declaration is the contract scripts read.
internal sealed class JsTaskBuilder
{
    private readonly Engine? _engine;

    /// <summary>
    /// A builder for <paramref name="engine"/>'s scripts: every refusal of a member a script calls
    /// is an <c>Error</c> the script catches (<see cref="JsHostError.Guard{T}"/>, #364). Built
    /// without an engine (a CLR caller), the refusals stay the raw CLR exceptions.
    /// </summary>
    public JsTaskBuilder(Engine? engine = null) => _engine = engine;

    // Every member a script calls goes through Guard (SCR-25): a CLR throw from a wrapped object
    // would cross the script's try ... catch and end the command.
    private TResult Guarded<TResult>(Func<TResult> body)
        => _engine is null ? body() : JsHostError.Guard(_engine, body);

    private string? _name;
    private string? _description;
    private JsAgent? _agent;
    private string? _expectedOutput;
    private readonly List<JsTask> _context = new();
    private JsValue? _expectSchema;
    private JsValue? _deliverableSpec;
    private bool _humanInput;
    private bool _asyncExecution;
    private Orkeon.Domain.Task.TaskRetryPolicy? _retry;
    private readonly List<JsValue> _tools = new();
    private string? _responseFormat;
    private string? _responseSchemaName;
    private JsValue? _responseSchema;
    private bool _responseSchemaStrict = true;
    private string? _profile;
    private object? _decisionSpec;
    private string? _whenTask;
    private object? _whenSpec;

    public JsTaskBuilder name(string value) => Guarded(() => { _name = value; return this; });
    public JsTaskBuilder description(string value) => Guarded(() => { _description = value; return this; });

    public JsTaskBuilder agent(JsAgent agent) => Guarded(() =>
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        return this;
    });

    public JsTaskBuilder expectedOutput(string value) => Guarded(() => { _expectedOutput = value; return this; });

    public JsTaskBuilder withContext(JsTask task) => Guarded(() =>
    {
        ArgumentNullException.ThrowIfNull(task);
        _context.Add(task);
        return this;
    });

    public JsTaskBuilder withContexts(JsValue tasks) => Guarded(() =>
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
    });

    /// <summary>YAML parity <c>humanInput: true</c> — the task pauses for the human-input provider.</summary>
    public JsTaskBuilder humanInput(bool value = true) => Guarded(() => { _humanInput = value; return this; });

    /// <summary>
    /// YAML parity <c>asyncExecution: true</c> — in a sequential crew the task runs alongside the tasks
    /// after it, and a task that lists it in <c>withContext</c> waits for it (GAP-22). A parallel crew
    /// accepts it without an effect of its own; the other modes refuse the crew.
    /// </summary>
    public JsTaskBuilder asyncExecution(bool value = true) => Guarded(() => { _asyncExecution = value; return this; });

    /// <summary>
    /// YAML parity task-level <c>tools:</c> — a name, a <c>toolBuilder()</c> tool, or an
    /// array mixing both. They add to the assigned agent's own tools for this task only
    /// (GAP-07). Names resolve through the runtime registry; instances are registered by
    /// the loader before the crew is created.
    /// </summary>
    public JsTaskBuilder tools(JsValue value) => Guarded(() =>
    {
        if (value is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
                _tools.Add(arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        else if (value is not null && !value.IsUndefined() && !value.IsNull())
        {
            _tools.Add(value);
        }
        return this;
    });

    public JsTaskBuilder expect(JsValue schema) => Guarded(() => { _expectSchema = schema; return this; });

    /// <summary>
    /// Forces the LLM output format on this task only (e.g. <c>"json_object"</c>).
    /// Stored as a string; the adapter materialises it into a
    /// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride"/> on the
    /// resulting <c>TaskConfiguration</c>. Pass <c>"text"</c> to keep the provider default.
    /// </summary>
    public JsTaskBuilder withResponseFormat(string type) => Guarded(() =>
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidScriptException(".withResponseFormat(type) requires a non-empty string.");
        _responseFormat = type;
        return this;
    });

    /// <summary>Exposes the captured response_format value to the adapter; <c>null</c> when unset.</summary>
    internal string? ResponseFormatValue => _responseFormat;

    /// <summary>
    /// Constrains this task's output to a JSON Schema, on the providers whose API validates
    /// one server-side. Implies <c>response_format: json_schema</c>.
    /// </summary>
    /// <param name="name">Schema name, required by the OpenAI dialect.</param>
    /// <param name="schema">The JSON Schema, as an object literal or a JSON string.</param>
    /// <param name="strict">Whether the provider must reject any deviation. Defaults to true.</param>
    public JsTaskBuilder withResponseSchema(string name, JsValue schema, JsValue? strict = null) => Guarded(() =>
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidScriptException(".withResponseSchema(name, schema) requires a non-empty name.");
        if (schema is null || schema.IsUndefined() || schema.IsNull())
            throw new InvalidScriptException(".withResponseSchema(name, schema) requires a schema object or JSON string.");

        _responseSchemaName = name;
        _responseSchema = schema;
        _responseSchemaStrict = strict is null || strict.IsUndefined() || strict.IsNull() || strict.AsBoolean();
        return this;
    });

    /// <summary>
    /// YAML parity <c>llm_override: { profile }</c> — runs this task alone on one of the host's named
    /// LLM profiles (<c>Llm:Profiles:&lt;name&gt;</c>), on that profile's own model; <c>"default"</c>
    /// brings it back to the host's default (GAP-19). The adapter carries the name to the task's
    /// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride"/>, and a name the host
    /// does not offer fails the load, listing the known ones — like a YAML task.
    /// </summary>
    public JsTaskBuilder withProfile(JsValue name) => Guarded(() =>
    {
        if (name is null || !name.IsString() || string.IsNullOrWhiteSpace(name.AsString()))
            throw new InvalidScriptException(".withProfile(name) takes the profile name as a non-empty string.");
        _profile = name.AsString().Trim();
        return this;
    });

    /// <summary>
    /// First-class deliverable contract — mirrors YAML's <c>deliverable: { ... }</c>
    /// block. Expected shape:
    /// <code>{ path: string, source: 'final_message' | 'structured_output' | 'tool_call' | 'none',
    ///         format?: 'markdown' | 'json' | 'text', sanitize?: boolean,
    ///         schemaPath?: string, schema?: object, schemaInline?: string }</code>
    /// The <c>schema</c> field (inline object) is JSON-serialized into
    /// <c>schemaInline</c> by the adapter — both names are accepted, <c>schema</c> wins.
    /// </summary>
    public JsTaskBuilder deliverable(JsValue spec) => Guarded(() =>
    {
        if (spec is null || spec.IsUndefined() || spec.IsNull())
            throw new InvalidScriptException(".deliverable(spec) requires a non-null spec object.");
        if (!spec.IsObject())
            throw new InvalidScriptException(".deliverable(spec) expects an object literal.");
        _deliverableSpec = spec;
        return this;
    });

    /// <summary>
    /// Makes this task a decision task: <c>{ questions, minConfidence, state, longContext, fallback }</c>,
    /// the shape of the YAML <c>decision:</c> block. It needs no agent; the domain checks every rule.
    /// </summary>
    public JsTaskBuilder decision(JsValue spec) => Guarded(() =>
    {
        if (spec is null || !spec.IsObject())
            throw new InvalidScriptException(".decision(spec) expects an object literal.");
        _decisionSpec = spec.ToObject();
        return this;
    });

    /// <summary>
    /// Runs this task only when the decision task named answered as asked: a clause object
    /// (<c>{ category: ["bug"], urgency: "&gt;= 0.7", "category.bug": "&gt;= 0.25" }</c>) or the string <c>"otherwise"</c>.
    /// </summary>
    public JsTaskBuilder when(string decisionTask, JsValue clauses) => Guarded(() =>
    {
        if (string.IsNullOrWhiteSpace(decisionTask))
            throw new InvalidScriptException(".when(task, clauses) requires the name of a decision task.");
        if (clauses is null || !(clauses.IsObject() || clauses.IsString()))
            throw new InvalidScriptException(".when(task, clauses) expects a clause object or \"otherwise\".");
        if (_whenTask is not null)
            throw new InvalidScriptException($".when(task, clauses) is called twice on task '{(string.IsNullOrWhiteSpace(_name) ? _description : _name)}': a task has one condition.");
        _whenTask = decisionTask;
        _whenSpec = clauses.ToObject();
        return this;
    });

    /// <summary>
    /// YAML parity <c>retry: {attempts, backoffMs, backoff}</c> (FWK-15): a failed attempt runs the whole
    /// task again, tool calls included, up to <c>attempts</c> runs in all. <c>attempts</c> is required;
    /// <c>backoffMs</c> (default 0) and <c>backoff</c> (<c>"fixed"</c>, the default, or
    /// <c>"exponential"</c>) set the wait between runs. A value the policy refuses fails here.
    /// </summary>
    public JsTaskBuilder retry(JsValue spec) => Guarded(() =>
    {
        if (spec is null || !spec.IsObject())
            throw new InvalidScriptException(".retry(spec) expects an object literal { attempts, backoffMs?, backoff? }.");

        var attemptsRaw = spec.Get("attempts");
        if (!attemptsRaw.IsNumber())
            throw new InvalidScriptException(".retry({ attempts }) is required: the number of times the task may run (1 or more).");

        var backoffMsRaw = spec.Get("backoffMs");
        var backoffRaw = spec.Get("backoff");
        var backoffText = backoffRaw.IsString() ? backoffRaw.AsString() : null;
        var backoff = backoffText switch
        {
            null or "fixed" => Orkeon.Domain.Task.TaskRetryBackoff.Fixed,
            "exponential" => Orkeon.Domain.Task.TaskRetryBackoff.Exponential,
            _ => throw new InvalidScriptException($".retry({{ backoff: \"{backoffText}\" }}) is unknown, use \"fixed\" or \"exponential\"."),
        };

        try
        {
            _retry = new Orkeon.Domain.Task.TaskRetryPolicy(
                (int)attemptsRaw.AsNumber(),
                backoffMsRaw.IsNumber() ? (int)backoffMsRaw.AsNumber() : 0,
                backoff);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidScriptException($".retry(...) is refused: {ex.ParamName} {ex.Message}", ex);
        }

        return this;
    });

    public JsTask build() => Guarded(() =>
    {
        if (_decisionSpec is not null)
        {
            if (string.IsNullOrWhiteSpace(_name))
                throw new InvalidScriptException("taskBuilder() decision task requires .name(...).");
            _description ??= _name;
            _expectedOutput ??= "decision";
        }

        if (_decisionSpec is not null && _retry is not null)
            throw new InvalidScriptException($"taskBuilder() decision task '{_name}' cannot have .retry(...): a decision runs no agent.");

        if (string.IsNullOrWhiteSpace(_description))
            throw new InvalidScriptException("taskBuilder() requires .description(...).");
        if (_agent is null && _decisionSpec is null)
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
            DeliverableSpec = _deliverableSpec,
            ResponseFormat = _responseFormat,
            ResponseSchemaName = _responseSchemaName,
            ResponseSchema = _responseSchema,
            ResponseSchemaStrict = _responseSchemaStrict,
            Profile = _profile,
            HumanInput = _humanInput,
            AsyncExecution = _asyncExecution,
            Retry = _retry,
            Tools = _tools,
            Key = string.IsNullOrWhiteSpace(_name) ? _description : _name,
            DecisionSpec = _decisionSpec,
            WhenTask = _whenTask,
            WhenSpec = _whenSpec,
        });
    });
}

internal static class TaskBuilderStringHelpers
{
    public static string TruncateForName(this string source, int max = 60)
        => source.Length <= max ? source : source[..max];
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
