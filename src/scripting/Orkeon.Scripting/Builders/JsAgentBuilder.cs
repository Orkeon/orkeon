using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainAgentRole = Orkeon.Domain.Agent.ValueObjects.AgentRole;
using DomainAgentGoal = Orkeon.Domain.Agent.ValueObjects.AgentGoal;
using DomainAgentBackstory = Orkeon.Domain.Agent.ValueObjects.AgentBackstory;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Fluent builder exposed to JS as <c>agentBuilder()</c>. Captures the configuration
/// supplied by the script and produces a <see cref="JsAgent"/> on <see cref="build"/>.
/// </summary>
/// <remarks>
/// SCR-03 minimum: captures and validates the builder surface. Body execution lands in
/// SCR-08, error policy in SCR-15, state mutation in SCR-12.
/// Method names are <c>camelCase</c> on purpose so they line up with the JS surface.
/// XML doc is intentionally light here — the canonical reference is the generated
/// <c>orkeon.d.ts</c> roll-up.
/// </remarks>
#pragma warning disable IDE1006 // Naming style aligned with the JS surface
#pragma warning disable CS1591 // JS-interop mirror of AgentBuilder in Typings/agent.d.ts; that declaration is the contract scripts read.
public sealed class JsAgentBuilder
{
    internal string? AgentName { get; private set; }
    internal string? AgentRole { get; private set; }
    internal string? AgentGoal { get; private set; }
    internal string? AgentBackstoryText { get; private set; }
    /// <summary>Tool names resolved by <c>IToolRegistry</c> at runtime — the YAML-parity
    /// surface populated by <c>agentBuilder().tools(["file_read", ...])</c>.</summary>
    internal List<string> BuiltInToolNames { get; } = new();
    internal List<JsValue> AutonomousTools { get; } = new();
    internal JsValue? LlmConfig { get; private set; }
    /// <summary>Captured <c>response_format</c> hint (e.g. <c>"json_object"</c>); merged onto LlmConfig by the adapter.</summary>
    internal string? ResponseFormatValue { get; private set; }

    /// <summary>Schema name captured by <c>withResponseSchema</c>; <c>null</c> when unset.</summary>
    internal string? ResponseSchemaName { get; private set; }

    /// <summary>Schema document captured by <c>withResponseSchema</c>, serialized by the adapter.</summary>
    internal JsValue? ResponseSchemaValue { get; private set; }

    /// <summary>Whether the captured schema is strict. Defaults to true.</summary>
    internal bool ResponseSchemaStrict { get; private set; } = true;
    internal bool AllowDelegationFlag { get; private set; }
    internal int MaxIterationsValue { get; private set; } = 20;
    internal bool VerboseFlag { get; private set; }
    internal int ConcurrencyValue { get; private set; } = 1;
    internal JsValue? StateFactory { get; private set; }
    internal JsValue? BodyFunction { get; private set; }
    internal JsValue? OnErrorHandler { get; private set; }
    internal JsValue? OnAgentStartHandler { get; private set; }
    internal JsValue? OnAgentStopHandler { get; private set; }
    internal JsValue? WhenPredicate { get; private set; }

    /// <summary>Declared <c>onCommand</c> handlers — the seam through which an agent answers dispatched commands (design §8 item 9).</summary>
    internal List<AgentCommandHandler> OnCommandHandlers { get; } = new();

    public JsAgentBuilder name(string value) { AgentName = value; return this; }
    public JsAgentBuilder role(string value) { AgentRole = value; return this; }
    public JsAgentBuilder goal(string value) { AgentGoal = value; return this; }
    public JsAgentBuilder backstory(string value) { AgentBackstoryText = value; return this; }

    /// <summary>
    /// Registers built-in tool names to resolve via <c>IToolRegistry</c> at runtime —
    /// exact mirror of YAML's <c>tools: [...]</c> on an agent definition. Accepts a
    /// single string, a JS array of strings, or a comma-separated list (rejected: tool
    /// instances belong to <see cref="withAutonomousTool"/>).
    /// </summary>
    public JsAgentBuilder tools(JsValue names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.IsString())
        {
            var single = names.AsString();
            if (!string.IsNullOrWhiteSpace(single))
                BuiltInToolNames.Add(single);
            return this;
        }
        if (names is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
            {
                var entry = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (entry.IsString())
                {
                    var s = entry.AsString();
                    if (!string.IsNullOrWhiteSpace(s))
                        BuiltInToolNames.Add(s);
                }
                else
                {
                    throw new InvalidScriptException(
                        ".tools(...) entries must be strings (built-in tool names). " +
                        "Use .withAutonomousTool(toolBuilder()…build()) for in-script tool instances.");
                }
            }
            return this;
        }
        throw new InvalidScriptException(
            ".tools(...) expects a string or a string[] (built-in tool names).");
    }

    public JsAgentBuilder withAutonomousTool(JsValue tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        AutonomousTools.Add(tool);
        return this;
    }

    public JsAgentBuilder withAutonomousTools(JsValue tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (tools is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
                AutonomousTools.Add(arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        else
        {
            AutonomousTools.Add(tools);
        }
        return this;
    }

    public JsAgentBuilder llm(JsValue config) { LlmConfig = config; return this; }
    public JsAgentBuilder withResponseFormat(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidScriptException(".withResponseFormat(type) requires a non-empty string.");
        ResponseFormatValue = type;
        return this;
    }

    /// <summary>
    /// Constrains this agent's output to a JSON Schema, on the providers whose API validates
    /// one server-side. Implies <c>response_format: json_schema</c>, so calling
    /// <c>withResponseFormat</c> as well is unnecessary.
    /// </summary>
    /// <param name="name">Schema name, required by the OpenAI dialect.</param>
    /// <param name="schema">The JSON Schema, as an object literal or a JSON string.</param>
    /// <param name="strict">Whether the provider must reject any deviation. Defaults to true.</param>
    public JsAgentBuilder withResponseSchema(string name, JsValue schema, JsValue? strict = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidScriptException(".withResponseSchema(name, schema) requires a non-empty name.");
        if (schema is null || schema.IsUndefined() || schema.IsNull())
            throw new InvalidScriptException(".withResponseSchema(name, schema) requires a schema object or JSON string.");

        ResponseSchemaName = name;
        ResponseSchemaValue = schema;
        ResponseSchemaStrict = strict is null || strict.IsUndefined() || strict.IsNull() || strict.AsBoolean();
        return this;
    }
    public JsAgentBuilder allowDelegation(bool value) { AllowDelegationFlag = value; return this; }
    public JsAgentBuilder maxIterations(int value) { MaxIterationsValue = value; return this; }
    public JsAgentBuilder verbose() { VerboseFlag = true; return this; }
    public JsAgentBuilder verbose(bool value) { VerboseFlag = value; return this; }
    public JsAgentBuilder withState(JsValue factoryOrSeed) { StateFactory = factoryOrSeed; return this; }
    public JsAgentBuilder concurrency(int value) { ConcurrencyValue = value; return this; }
    public JsAgentBuilder body(JsValue fn) { BodyFunction = fn; return this; }
    public JsAgentBuilder onError(JsValue handler) { OnErrorHandler = handler; return this; }
    public JsAgentBuilder onAgentStart(JsValue handler) { OnAgentStartHandler = handler; return this; }
    public JsAgentBuilder onAgentStop(JsValue handler) { OnAgentStopHandler = handler; return this; }
    public JsAgentBuilder when(JsValue predicate) { WhenPredicate = predicate; return this; }

    /// <summary>
    /// Declares that this agent answers dispatched commands (design §8 item 9). With one
    /// argument the handler answers any intent; with two, the first is the intent it answers.
    /// The handler receives the command envelope and returns the response payload (string) or
    /// <c>{ success?, payload, error? }</c>.
    /// </summary>
    public JsAgentBuilder onCommand(JsValue handler)
    {
        RequireFunction(handler, ".onCommand(handler)");
        OnCommandHandlers.Add(new AgentCommandHandler(null, handler));
        return this;
    }

    public JsAgentBuilder onCommand(string intent, JsValue handler)
    {
        if (string.IsNullOrWhiteSpace(intent))
            throw new InvalidScriptException(".onCommand(intent, handler) requires a non-empty intent.");
        RequireFunction(handler, ".onCommand(intent, handler)");
        OnCommandHandlers.Add(new AgentCommandHandler(intent, handler));
        return this;
    }

    private static void RequireFunction(JsValue handler, string where)
    {
        if (handler is null || handler.IsUndefined() || handler.IsNull() || handler is not Jint.Native.Function.Function)
            throw new InvalidScriptException($"{where} requires a function.");
    }

    public JsAgent build()
    {
        if (string.IsNullOrWhiteSpace(AgentName))
            throw new InvalidScriptException("agentBuilder() requires .name(...).");
        if (string.IsNullOrWhiteSpace(AgentRole))
            throw new InvalidScriptException("agentBuilder() requires .role(...).");
        if (string.IsNullOrWhiteSpace(AgentGoal))
            throw new InvalidScriptException("agentBuilder() requires .goal(...).");
        if (MaxIterationsValue <= 0)
            throw new InvalidScriptException(".maxIterations must be positive.");
        if (ConcurrencyValue <= 0)
            throw new InvalidScriptException(".concurrency must be positive.");
        if (ConcurrencyValue > 1)
            throw new InvalidScriptException(
                "V1 supports .concurrency(1) only (mutex). N-holders semaphore is deferred to V1.5.");

        var domain = DomainAgent.Create(
            role: DomainAgentRole.From(AgentRole!),
            goal: DomainAgentGoal.From(AgentGoal!),
            backstory: string.IsNullOrWhiteSpace(AgentBackstoryText) ? null : DomainAgentBackstory.From(AgentBackstoryText!),
            allowDelegation: AllowDelegationFlag,
            maxIterations: MaxIterationsValue,
            verbose: VerboseFlag);

        return new JsAgent(AgentName!, domain, this);
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
