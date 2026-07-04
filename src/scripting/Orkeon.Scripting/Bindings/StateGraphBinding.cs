using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Orchestration;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>stateGraph(literal)</c> factory plus <c>START</c> /
/// <c>END</c> sentinels.
/// </summary>
public static class StateGraphBinding
{
    /// <summary>Global function name.</summary>
    public const string GlobalName = "stateGraph";

    /// <summary>Adds <c>stateGraph</c>, <c>START</c>, <c>END</c> to <paramref name="engine"/>.</summary>
    public static void Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetValue("START", GraphSentinels.Start);
        engine.SetValue("END", GraphSentinels.End);
        engine.SetValue(GlobalName, new Func<JsValue, JsStateGraph>(literal => Build(engine, literal)));
    }

    private static JsStateGraph Build(Engine engine, JsValue literal)
    {
        if (literal is null || !literal.IsObject())
            throw new InvalidScriptException("stateGraph() expects a literal { name, nodes, edges }.");

        var nameVal = literal.Get("name");
        var nodesVal = literal.Get("nodes");
        var edgesVal = literal.Get("edges");
        if (!nameVal.IsString())
            throw new InvalidScriptException("stateGraph literal requires a string 'name'.");
        if (!nodesVal.IsObject())
            throw new InvalidScriptException("stateGraph literal requires a 'nodes' object.");
        if (!edgesVal.IsObject())
            throw new InvalidScriptException("stateGraph literal requires an 'edges' object.");

        var nodes = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        foreach (var prop in nodesVal.AsObject().GetOwnProperties())
        {
            var nodeName = prop.Key.ToString()!;
            if (nodeName == GraphSentinels.Start || nodeName == GraphSentinels.End)
                throw new InvalidScriptException($"Reserved node id '{nodeName}' is not allowed.");
            nodes[nodeName] = prop.Value.Value;
        }

        var edges = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        foreach (var prop in edgesVal.AsObject().GetOwnProperties())
            edges[prop.Key.ToString()!] = prop.Value.Value;

        if (!edges.ContainsKey(GraphSentinels.Start))
            throw new InvalidScriptException("stateGraph literal must declare an edge from START.");

        ValidateEdgeTargets(nodes, edges);
        ValidateReachability(edges);

        var config = ParseConfig(literal.Get("graphConfig"));

        // Bind the graph to whichever agent body is currently running so that run()
        // picks up the ambient CT without an explicit options argument.
        var brokerHolder = TryFindBrokerOnEngine(engine);
        Func<CancellationToken>? ambientCt = brokerHolder is null
            ? null
            : () => brokerHolder.CurrentCt;

        return new JsStateGraph(engine, nameVal.AsString(), nodes, edges, config, ambientCt);
    }

    private static void ValidateEdgeTargets(
        Dictionary<string, JsValue> nodes, Dictionary<string, JsValue> edges)
    {
        foreach (var (from, edge) in edges)
        {
            if (!edge.IsString()) continue; // conditional edges validated at run-time
            var target = edge.AsString();
            if (target == GraphSentinels.End) continue;
            if (!nodes.ContainsKey(target))
                throw new InvalidScriptException(
                    $"stateGraph edge from '{from}' targets undeclared node '{target}'.");
        }
    }

    private static void ValidateReachability(Dictionary<string, JsValue> edges)
    {
        // BFS from START following only string edges. Conditional (function) edges are
        // assumed potentially reachable — too imprecise to evaluate statically. We do not
        // require a static path from every node, only that END is reachable from START.
        var visited = new HashSet<string>(StringComparer.Ordinal) { GraphSentinels.Start };
        var queue = new Queue<string>();
        queue.Enqueue(GraphSentinels.Start);
        var endReachable = false;

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!edges.TryGetValue(node, out var edge)) continue;
            if (!edge.IsString())
            {
                // Conditional edge: we can't know where it points; conservatively assume
                // END is reachable from here.
                endReachable = true;
                continue;
            }
            var target = edge.AsString();
            if (target == GraphSentinels.End) { endReachable = true; continue; }
            if (visited.Add(target)) queue.Enqueue(target);
        }

        if (!endReachable)
            throw new InvalidScriptException(
                "stateGraph has no path from START to END.");
    }

    private static JsGraphConfig ParseConfig(JsValue cfg)
    {
        // Defaults map to the "Default" preset of Orkeon.Domain.Configuration.GraphConfig.
        var maxTransitions = 200;
        var maxStateVisits = int.MaxValue;
        var maxRetryCycles = int.MaxValue;
        TimeSpan? maxTotalDuration = null;

        if (cfg.IsObject())
        {
            var preset = cfg.Get("circuitBreakerPreset");
            if (preset.IsString())
            {
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
                switch (preset.AsString().ToLowerInvariant())
#pragma warning restore CA1308
                {
                    case "strict":
                        maxTransitions = 50;
                        maxStateVisits = 3;
                        maxRetryCycles = 1;
                        maxTotalDuration = TimeSpan.FromSeconds(30);
                        break;
                    case "default":
                        maxTransitions = 200;
                        maxStateVisits = 10;
                        maxRetryCycles = 5;
                        maxTotalDuration = TimeSpan.FromMinutes(5);
                        break;
                    case "permissive":
                        maxTransitions = 1000;
                        maxStateVisits = 100;
                        maxRetryCycles = 50;
                        maxTotalDuration = TimeSpan.FromMinutes(30);
                        break;
                    default:
                        throw new InvalidScriptException(
                            $"Unknown circuitBreakerPreset '{preset.AsString()}'. Expected Strict|Default|Permissive.");
                }
            }

            var mt = cfg.Get("maxTransitions");
            if (mt.IsNumber()) maxTransitions = (int)mt.AsNumber();
            var msv = cfg.Get("maxStateVisits");
            if (msv.IsNumber()) maxStateVisits = (int)msv.AsNumber();
            var mrc = cfg.Get("maxRetryCycles");
            if (mrc.IsNumber()) maxRetryCycles = (int)mrc.AsNumber();
            var mtd = cfg.Get("maxTotalDurationSeconds");
            if (mtd.IsNumber()) maxTotalDuration = TimeSpan.FromSeconds(mtd.AsNumber());
        }

        return new JsGraphConfig(maxTransitions, maxStateVisits, maxRetryCycles, maxTotalDuration);
    }

    /// <summary>
    /// Walks the engine's globals looking for an event broker (planted on the agent
    /// context that captured the engine). Returns null when stateGraph is built outside
    /// a body — the run() will then operate without an ambient CT.
    /// </summary>
    private static JsEventBroker? TryFindBrokerOnEngine(Engine engine)
    {
        // The broker is reachable transitively through any JsAgentContext registered as
        // a global. We don't pin it; we just lazily resolve from the running engine via a
        // well-known symbol — set up by JsExecutionContext when present.
        var holder = engine.GetValue("__orkeon_broker");
        if (holder.IsUndefined() || holder.IsNull()) return null;
        return holder.ToObject() as JsEventBroker;
    }
}
