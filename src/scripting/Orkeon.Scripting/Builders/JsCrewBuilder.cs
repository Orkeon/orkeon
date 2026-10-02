using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Fluent builder exposed to JS as <c>crewBuilder()</c>. Captures the configuration
/// supplied by the script and produces a <see cref="JsCrew"/> on <see cref="build"/>.
/// </summary>
#pragma warning disable IDE1006 // Method names match the JS surface
#pragma warning disable CS1591 // JS-interop mirror of CrewBuilder in Typings/crew.d.ts; that declaration is the contract scripts read.
public sealed partial class JsCrewBuilder
{
    private static readonly HashSet<string> AllowedProcesses = new(StringComparer.Ordinal)
    {
        "sequential", "hierarchical", "parallel", "consensual", "graph", "autonomous",
    };

    private readonly Engine _engine;
    private readonly ILogger _logger;
    private readonly ILlmProvider? _llmProvider;
    private readonly IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? _builtInTools;
    private readonly Orkeon.Application.Interfaces.Security.IPermissionGate? _permissionGate;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? _deltaSink;
    private readonly Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? _toolInvocation;
    private string _name = "crew";
    private string? _goal;
    private string _process = "sequential";
    private readonly List<JsAgent> _agents = new();
    private readonly List<JsTask> _tasks = new();
    private JsAgent? _manager;
    private readonly Dictionary<string, object?> _budget = new();
    private bool _verbose;
    private bool _memory;
    private JsValue? _onCrewStart, _onCrewComplete, _onCrewError;

    public JsCrewBuilder(
        Engine engine,
        ILogger? logger = null,
        ILlmProvider? llmProvider = null,
        IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? builtInTools = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null,
        Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? deltaSink = null,
        Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? toolInvocation = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? NullLogger.Instance;
        _llmProvider = llmProvider;
        _builtInTools = builtInTools;
        _permissionGate = permissionGate;
        _deltaSink = deltaSink;
        _toolInvocation = toolInvocation;
    }

    public JsCrewBuilder name(string value) { _name = value; return this; }

    public JsCrewBuilder goal(string value) { _goal = value; return this; }

    public JsCrewBuilder process(string value)
    {
        if (!AllowedProcesses.Contains(value))
            throw new InvalidScriptException($"Unknown process '{value}'. Expected one of: {string.Join(", ", AllowedProcesses)}.");
        _process = value;
        return this;
    }

    // withAgent / withTask take BUILT values. The typings used to offer a builder callback
    // (`withTask(b => b.description(...))`): withAgent threw a conversion error on it and
    // withTask stored the function, which the adapter then filtered out — the task vanished
    // without a word (GAP-12). Anything that is not an agent or a task is refused here.
    public JsCrewBuilder withAgent(JsValue agent)
    {
        _agents.Add(RequireBuilt<JsAgent>(agent, ".withAgent(agent)", "agentBuilder()…build()"));
        return this;
    }

    public JsCrewBuilder withAgents(JsValue agents)
    {
        foreach (var entry in RequireArray(agents, ".withAgents(agents)"))
            _agents.Add(RequireBuilt<JsAgent>(entry, ".withAgents(agents)", "agentBuilder()…build()"));
        return this;
    }

    public JsCrewBuilder withTask(JsValue task)
    {
        _tasks.Add(RequireBuilt<JsTask>(task, ".withTask(task)", "taskBuilder()…build()"));
        return this;
    }

    public JsCrewBuilder withTasks(JsValue tasks)
    {
        foreach (var entry in RequireArray(tasks, ".withTasks(tasks)"))
            _tasks.Add(RequireBuilt<JsTask>(entry, ".withTasks(tasks)", "taskBuilder()…build()"));
        return this;
    }

    private static T RequireBuilt<T>(JsValue? value, string where, string how) where T : class
        => value?.ToObject() as T
           ?? throw new InvalidScriptException($"{where} expects what {how} returns; a builder callback or a plain object is not accepted.");

    private static IEnumerable<JsValue> RequireArray(JsValue? value, string where)
    {
        if (value is not Jint.Native.Array.ArrayInstance arr)
            throw new InvalidScriptException($"{where} expects an array.");
        var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
        for (uint i = 0; i < len; i++)
            yield return arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public JsCrewBuilder manager(JsAgent agent) { _manager = agent; return this; }

    public JsCrewBuilder budget(JsValue spec)
    {
        if (spec is not null && spec.IsObject())
        {
            foreach (var prop in spec.AsObject().GetOwnProperties())
            {
                _budget[prop.Key.ToString()!] = prop.Value.Value.ToObject();
            }
        }
        return this;
    }

    /// <summary>
    /// YAML parity <c>memory: true</c> — the crew remembers: each run stores the result of its tasks
    /// and recalls the closest ones before each task, in the host's default memory store, under the
    /// crew's name (GAP-30). Off by default. Not <c>ctx.memory.*</c>, the scoped key/value stores of a run.
    /// </summary>
    public JsCrewBuilder memory(bool value = true) { _memory = value; return this; }

    public JsCrewBuilder verbose() { _verbose = true; return this; }
    public JsCrewBuilder verbose(bool value) { _verbose = value; return this; }
    public JsCrewBuilder onCrewStart(JsValue hook) { _onCrewStart = hook; return this; }
    public JsCrewBuilder onCrewComplete(JsValue hook) { _onCrewComplete = hook; return this; }
    public JsCrewBuilder onCrewError(JsValue hook) { _onCrewError = hook; return this; }

    public JsCrew build()
    {
        if (string.Equals(_process, "hierarchical", StringComparison.Ordinal) && _manager is null)
            throw new InvalidScriptException("crewBuilder().process(\"hierarchical\") requires .manager(agent).");

        WarnOnAutonomousToolsWithoutSchema();

        var crew = new JsCrew(_engine, new JsCrewDefinition
        {
            Name = _name,
            Process = _process,
            Agents = _agents,
            Tasks = _tasks,
            Manager = _manager,
            Budget = _budget,
            Verbose = _verbose,
            Memory = _memory,
            Logger = _logger,
            LlmProvider = _llmProvider,
            BuiltInTools = _builtInTools,
            PermissionGate = _permissionGate,
            DeltaSink = _deltaSink,
            ToolInvocation = _toolInvocation,
            Goal = _goal,
        });
        crew._onCrewStart = _onCrewStart;
        crew._onCrewComplete = _onCrewComplete;
        crew._onCrewError = _onCrewError;
        return crew;
    }

    private void WarnOnAutonomousToolsWithoutSchema()
    {
        foreach (var agent in _agents)
        {
            foreach (var tool in agent.Builder.AutonomousTools)
            {
                if (!tool.HasExplicitSchema)
                {
                    LogAutonomousToolWithoutSchema(tool.Name, agent.name);
                }
            }
        }
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Tool '{ToolName}' is registered as autonomous on agent '{AgentName}' but has no schema. " +
            "LLMs may fail to call it correctly. Add .withSchema({{...}}) to fix.")]
    private partial void LogAutonomousToolWithoutSchema(string toolName, string agentName);
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
