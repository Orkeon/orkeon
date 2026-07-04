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
#pragma warning disable CS1591
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
    private string _name = "crew";
    private string? _goal;
    private string _process = "sequential";
    private readonly List<JsAgent> _agents = new();
    private readonly List<object> _tasks = new();
    private JsAgent? _manager;
    private readonly Dictionary<string, object?> _budget = new();
    private object? _graph;
    private bool _verbose;
    private JsValue? _onCrewStart, _onCrewComplete, _onCrewError;

    public JsCrewBuilder(
        Engine engine,
        ILogger? logger = null,
        ILlmProvider? llmProvider = null,
        IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? builtInTools = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null,
        Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? deltaSink = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? NullLogger.Instance;
        _llmProvider = llmProvider;
        _builtInTools = builtInTools;
        _permissionGate = permissionGate;
        _deltaSink = deltaSink;
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

    public JsCrewBuilder withAgent(JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agents.Add(agent);
        return this;
    }

    public JsCrewBuilder withAgents(JsValue agents)
    {
        if (agents is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
            {
                var a = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToObject();
                if (a is JsAgent ja) _agents.Add(ja);
            }
        }
        return this;
    }

    public JsCrewBuilder withTask(JsValue task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _tasks.Add(task.ToObject() ?? task);
        return this;
    }

    public JsCrewBuilder withTasks(JsValue tasks)
    {
        if (tasks is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
                _tasks.Add(arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToObject() ?? arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return this;
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

    public JsCrewBuilder graph(JsValue graphInstance)
    {
        ArgumentNullException.ThrowIfNull(graphInstance);
        _graph = graphInstance.ToObject();
        return this;
    }

    public JsCrewBuilder verbose() { _verbose = true; return this; }
    public JsCrewBuilder verbose(bool value) { _verbose = value; return this; }
    public JsCrewBuilder when(JsValue predicate) { _ = predicate; return this; }
    public JsCrewBuilder onCrewStart(JsValue hook) { _onCrewStart = hook; return this; }
    public JsCrewBuilder onCrewComplete(JsValue hook) { _onCrewComplete = hook; return this; }
    public JsCrewBuilder onCrewError(JsValue hook) { _onCrewError = hook; return this; }

    public JsCrew build()
    {
        if (string.Equals(_process, "hierarchical", StringComparison.Ordinal) && _manager is null)
            throw new InvalidScriptException("crewBuilder().process(\"hierarchical\") requires .manager(agent).");
        if (string.Equals(_process, "graph", StringComparison.Ordinal) && _graph is null)
            throw new InvalidScriptException("crewBuilder().process(\"graph\") requires .graph(stateGraph).");

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
            Logger = _logger,
            LlmProvider = _llmProvider,
            BuiltInTools = _builtInTools,
            PermissionGate = _permissionGate,
            DeltaSink = _deltaSink,
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
            foreach (var raw in agent.Builder.AutonomousTools)
            {
                if (raw.ToObject() is JsTool tool && !tool.HasExplicitSchema)
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
