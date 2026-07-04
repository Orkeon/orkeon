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
        Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? deltaSink = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var tools = builtInTools as IReadOnlyList<IBaseTool> ?? builtInTools?.ToArray();
        engine.SetValue(GlobalName, new Func<JsCrewBuilder>(() => new JsCrewBuilder(engine, logger, llmProvider, tools, permissionGate, deltaSink)));
    }
}
