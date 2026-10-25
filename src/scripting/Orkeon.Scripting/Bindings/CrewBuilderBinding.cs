using Jint;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>crewBuilder()</c> function on a Jint engine.
/// </summary>
public static class CrewBuilderBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "crewBuilder";

    /// <summary>
    /// Adds <c>crewBuilder()</c> to <paramref name="engine"/>'s global scope. The optional
    /// <paramref name="logger"/> receives warnings emitted by <c>JsCrewBuilder.build()</c>
    /// (notably the autonomous-tool-without-schema warning from SCR-06). The optional
    /// <paramref name="llmProvider"/> is threaded down to <c>JsCrew</c> so agent bodies
    /// can call <c>ctx.llm.complete</c> against a real provider instead of the
    /// <c>UndefinedLlm</c> echo fallback.
    /// </summary>
    public static void Register(
        Engine engine,
        ILogger? logger = null,
        ILlmProvider? llmProvider = null,
        IEnumerable<IBaseTool>? builtInTools = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null,
        Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? deltaSink = null,
        Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? toolInvocation = null)
        => Register(engine, new CrewBuilderServices
        {
            Logger = logger,
            LlmProvider = llmProvider,
            BuiltInTools = builtInTools,
            PermissionGate = permissionGate,
            DeltaSink = deltaSink,
            ToolInvocation = toolInvocation,
        });

    internal static void Register(Engine engine, CrewBuilderServices services)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(services);
        var tools = services.BuiltInTools as IReadOnlyList<IBaseTool> ?? services.BuiltInTools?.ToArray();
        engine.SetValue(GlobalName, new Func<JsCrewBuilder>(() => new JsCrewBuilder(
            engine, services.Logger, services.LlmProvider, tools, services.PermissionGate, services.DeltaSink, services.ToolInvocation)
        {
            AgentMemoryStore = services.AgentMemoryStore,
            ToolLimits = services.ToolLimits,
            AgentInstances = services.AgentInstances,
            Mounts = services.Mounts,
        }));
    }

    /// <summary>What every crew builder of the engine is handed: each one optional.</summary>
    internal sealed record CrewBuilderServices
    {
        public ILogger? Logger { get; init; }
        public ILlmProvider? LlmProvider { get; init; }
        public IEnumerable<IBaseTool>? BuiltInTools { get; init; }
        public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }
        public Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? DeltaSink { get; init; }
        public Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? ToolInvocation { get; init; }
        public Orkeon.Application.Interfaces.Ports.IScriptAgentMemoryStore? AgentMemoryStore { get; init; }
        public Orkeon.Domain.Tools.ToolResultLimitsOptions? ToolLimits { get; init; }
        public Orkeon.Scripting.Configuration.ScriptAgentInstanceOptions? AgentInstances { get; init; }
        public Func<IReadOnlyList<Orkeon.Domain.FileSystem.MountInfo>>? Mounts { get; init; }
    }
}
