using Jint;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Configuration;

namespace Orkeon.Scripting;

/// <summary>
/// Builds <see cref="Engine"/> instances with sandbox limits drawn from
/// <see cref="ScriptingLimitsOptions"/>. Each script execution should use a fresh engine
/// to avoid leaking globals or recursion budget between runs.
/// </summary>
public sealed class JsEngineFactory
{
    private readonly ScriptingLimitsOptions _limits;
    private readonly ILogger? _scriptLogger;
    private readonly IConfiguration? _configuration;
    private readonly IEnumerable<IBaseTool>? _builtInTools;
    private readonly Orkeon.Domain.SharedKernel.ILlmProvider? _llmProvider;
    private readonly Orkeon.Application.Interfaces.Security.IPermissionGate? _permissionGate;

    /// <summary>
    /// Creates a factory that builds engines respecting <paramref name="limits"/>.
    /// </summary>
    public JsEngineFactory(
        IOptions<ScriptingLimitsOptions> limits,
        ILoggerFactory? loggerFactory = null,
        IConfiguration? configuration = null,
        IEnumerable<IBaseTool>? builtInTools = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? llmProvider = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits.Value;
        _scriptLogger = loggerFactory?.CreateLogger("Orkeon.Scripting");
        _configuration = configuration;
        _builtInTools = builtInTools;
        _llmProvider = llmProvider;
        _permissionGate = permissionGate;
    }

    /// <summary>
    /// Convenience constructor used by tests and by hosts that don't go through DI:
    /// builds a factory with the supplied (or default) limits.
    /// </summary>
    public JsEngineFactory(
        ScriptingLimitsOptions? limits = null,
        ILoggerFactory? loggerFactory = null,
        IConfiguration? configuration = null,
        IEnumerable<IBaseTool>? builtInTools = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? llmProvider = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null)
        : this(Microsoft.Extensions.Options.Options.Create(limits ?? new ScriptingLimitsOptions()), loggerFactory, configuration, builtInTools, llmProvider, permissionGate)
    {
    }

    /// <summary>
    /// Creates a fresh, isolated <see cref="Engine"/> with the configured sandbox limits.
    /// </summary>
    public Engine Create()
    {
        var engine = new Engine(opt => opt
            .LimitMemory(_limits.MemoryLimitBytes)
            .LimitRecursion(_limits.RecursionLimit)
            .TimeoutInterval(_limits.ExecutionTimeout));

        // Register globals exposed to every script. Bindings are added incrementally as
        // builders land (SCR-03..SCR-06).
        AgentBuilderBinding.Register(engine);
        CrewBuilderBinding.Register(engine, _scriptLogger, _llmProvider, _builtInTools, _permissionGate);
        TaskBuilderBinding.Register(engine);
        ToolBuilderBinding.Register(engine);
        LlmNamespaceBinding.Register(engine, _configuration, _scriptLogger, _llmProvider);
        ToolsNamespaceBinding.Register(engine, _builtInTools ?? Array.Empty<IBaseTool>(), _scriptLogger);
        ErrorActionBinding.Register(engine);
        StateMachineBinding.Register(engine);
        StateGraphBinding.Register(engine);

        return engine;
    }
}
