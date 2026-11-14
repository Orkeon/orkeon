using System.Text.Json;
using Jint;
using Jint.Native;
using Orkeon.Application.Runs;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Builders;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Scripting.Runtime;

/// <summary>Request of the script host's <c>spawn_agent</c> tool.</summary>
internal sealed record SpawnAgentScriptRequest
{
    /// <summary>Gets the name of the crew agent to run as a child.</summary>
    [FieldSchema(Description = "Name of the crew agent to run as a child", Example = "researcher")]
    public string Agent { get; init; } = "";

    /// <summary>Gets the input handed to the child.</summary>
    [FieldSchema(Description = "Input handed to the child agent", Example = "Summarise the findings")]
    public string Input { get; init; } = "";
}

/// <summary>Response of the script host's <c>spawn_agent</c> tool.</summary>
internal sealed record SpawnAgentScriptResponse
{
    /// <summary>Gets the child's output.</summary>
    [ReturnSchema(Description = "Output of the child agent")]
    public string Output { get; init; } = "";
}

/// <summary>
/// The model-callable <c>spawn_agent</c> of the script host: runs a named crew agent as a child on the
/// engine, under a child budget (depth + 1). The run is engine work: the act loop reaches it through its
/// JS tool pump (<see cref="Start(string, string?)"/>), never from the loop's own thread.
/// </summary>
[ToolContract("spawn_agent", Name = "Spawn sub-agent",
    Description = "Run a named agent of the crew as a child and return its output",
    Category = "Autonomous")]
internal sealed class SpawnAgentScriptTool : ToolBase<SpawnAgentScriptRequest, SpawnAgentScriptResponse>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly JsExecutionEnvironment _environment;
    private readonly AgentExecutionBudget _budget;

    internal SpawnAgentScriptTool(JsExecutionEnvironment environment)
    {
        _environment = environment;
        _budget = environment.Budget ?? AgentExecutionBudget.Default;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <summary>
    /// Starts the child on the engine thread and returns its promise. Refuses an unknown agent, and a
    /// spawn past <see cref="AgentExecutionBudget.MaxDelegationDepth"/> or
    /// <see cref="AgentExecutionBudget.MaxSpawnedAgents"/>, by throwing.
    /// </summary>
    /// <param name="argumentsJson">The <c>spawn_agent</c> arguments.</param>
    /// <param name="spawnerMode">The permission mode of the act that spawns the child (SEC-7).</param>
    internal JsValue Start(string argumentsJson, string? spawnerMode = null)
    {
        var request = JsonSerializer.Deserialize<SpawnAgentScriptRequest>(argumentsJson, Json) ?? new SpawnAgentScriptRequest();
        return Start(request.Agent, request.Input, inheritPermit: true, _environment.Ct, spawnerMode);
    }

    /// <summary>
    /// Starts the child under <paramref name="signal"/>, through the crew's concurrency gate, and
    /// returns its promise; refuses as <see cref="Start(string, string?)"/> does. With
    /// <paramref name="inheritPermit"/> the child runs under the permit its caller holds (the blocking
    /// spawn); without it the child takes a permit of its own (a background spawn). SEC-7: the child's
    /// acts are held to the stricter of their own mode and <paramref name="spawnerMode"/> (the spawner
    /// run's own floor when null), as a crew delegation is.
    /// </summary>
    internal JsValue Start(string agentName, string input, bool inheritPermit, CancellationToken signal, string? spawnerMode = null)
    {
        var crew = _environment.Crew;
        var agent = crew.Resolve(agentName);
        if (_budget.MaxDelegationDepth <= 0)
            throw new BudgetExhaustedException(BudgetDimension.DelegationDepth,
                $"Delegation depth budget exhausted: cannot spawn '{agentName}' (max {_budget.MaxDelegationDepth}).");
        var childBudget = _budget.CreateChildBudget();
        _budget.RecordSpawn();

        var engine = _environment.Engine;
        var instances = crew.Instances;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(signal);
        var inheritedRoot = _environment.WorkingRoot;
        var floor = spawnerMode ?? _environment.PermissionFloor;
        var inheritedFileSystem = _environment.FileSystem;
        var instanceId = instances.Register(agent, childBudget, stop, inheritedRoot, floor, inheritedFileSystem);
        ReportSpawn(agent);
        var options = new Jint.Native.JsObject(engine);
        options.Set("signal", JsValue.FromObject(engine, stop.Token));
        options.Set("budget", JsValue.FromObject(engine, childBudget));
        options.Set("instance", JsValue.FromObject(engine, new AgentInstanceRun(instances, instanceId, [])));
        if (inheritPermit && _environment.Permit is { } permit)
            options.Set("permit", JsValue.FromObject(engine, permit));
        // RT-06: a child without a working root of its own runs under its parent's; #349: its
        // file-system scope can only narrow its parent's.
        if (inheritedRoot is not null || inheritedFileSystem is not null)
            options.Set("root", JsValue.FromObject(engine, new InheritedWorkingRoot(inheritedRoot, inheritedFileSystem)));
        if (floor is not null)
            options.Set("permissionFloor", floor);
        JsValue promise;
        try { promise = engine.Invoke(crew.runChild, [JsValue.FromObject(engine, agent), input, options]); }
        catch
        {
            instances.Finish(instanceId, succeeded: false);
            throw;
        }

        var onCompleted = new Action<JsValue>(_ => instances.Finish(instanceId, succeeded: true));
        var onFailed = new Action<JsValue>(_ => instances.Finish(instanceId, succeeded: false));
        engine.Invoke(promise.Get("then"), promise, [JsValue.FromObject(engine, onCompleted), JsValue.FromObject(engine, onFailed)]);
        return promise;
    }

    /// <summary>
    /// Tells the run's event stream the team grew (<c>agent.spawned</c>), with the keys the hosting
    /// decorator gives an observed <c>spawn_agent</c> call (<c>role</c>, <c>reason</c>, here absent) plus the
    /// crew agent's <c>name</c>. Never the input: the stream is read by a UI, and the input can carry anything.
    /// This tool is built per context with <c>new</c>, so no decorator wraps it and this is its only report.
    /// </summary>
    private void ReportSpawn(JsAgent agent)
    {
        if (_environment.RunEvents is not { } events)
            return;
        events.Emit(
            Orkeon.Constants.Protocol.RunEventKinds.AgentSpawned,
            new RunEventScope { CorrelationId = Guid.NewGuid().ToString("N") },
            new { role = agent.role, name = agent.name });
    }

    /// <inheritdoc />
    protected override Task<SpawnAgentScriptResponse> ExecuteTypedAsync(SpawnAgentScriptRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("spawn_agent runs on the engine, through the act call's tool pump.");
}
