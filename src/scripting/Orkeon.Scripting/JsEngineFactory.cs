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
    private readonly Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? _deltaSink;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmUsageSink? _usageSink;
    private readonly Bindings.RagScriptingBackend? _ragBackend;

    /// <summary>
    /// Creates a factory that builds engines respecting <paramref name="limits"/>.
    /// </summary>
    /// <param name="limits">Sandbox limits applied to every engine this factory builds.</param>
    /// <param name="loggerFactory">Source of the script-facing logger; null silences it.</param>
    /// <param name="configuration">Configuration the <c>llm</c> namespace reads its defaults from.</param>
    /// <param name="builtInTools">Built-in tools exposed to scripts through the <c>tools</c> namespace.</param>
    /// <param name="llmProvider">Provider backing <c>ctx.llm</c>; null keeps the undefined-LLM echo behaviour.</param>
    /// <param name="hostPorts">Permission gate and telemetry sinks the host wired, if any.</param>
    /// <param name="ragBackend">Pipelines + VFS backing the <c>rag</c> namespace; null makes its calls fail loudly.</param>
    public JsEngineFactory(
        IOptions<ScriptingLimitsOptions> limits,
        ILoggerFactory? loggerFactory = null,
        IConfiguration? configuration = null,
        IEnumerable<IBaseTool>? builtInTools = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? llmProvider = null,
        ScriptingHostPorts? hostPorts = null,
        Bindings.RagScriptingBackend? ragBackend = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits.Value;
        _scriptLogger = loggerFactory?.CreateLogger("Orkeon.Scripting");
        _configuration = configuration;
        _builtInTools = builtInTools;
        _llmProvider = llmProvider;
        _permissionGate = hostPorts?.PermissionGate;
        _deltaSink = hostPorts?.DeltaSink;
        _usageSink = hostPorts?.UsageSink;
        _ragBackend = ragBackend;
    }

    /// <summary>
    /// Convenience constructor used by tests and by hosts that don't go through DI:
    /// builds a factory with the supplied (or default) limits.
    /// </summary>
    /// <param name="limits">Sandbox limits; null applies the untrusted-script defaults.</param>
    /// <param name="loggerFactory">Source of the script-facing logger; null silences it.</param>
    /// <param name="configuration">Configuration the <c>llm</c> namespace reads its defaults from.</param>
    /// <param name="builtInTools">Built-in tools exposed to scripts through the <c>tools</c> namespace.</param>
    /// <param name="llmProvider">Provider backing <c>ctx.llm</c>; null keeps the undefined-LLM echo behaviour.</param>
    /// <param name="hostPorts">Permission gate and telemetry sinks the host wired, if any.</param>
    /// <param name="ragBackend">Pipelines + VFS backing the <c>rag</c> namespace; null makes its calls fail loudly.</param>
    public JsEngineFactory(
        ScriptingLimitsOptions? limits = null,
        ILoggerFactory? loggerFactory = null,
        IConfiguration? configuration = null,
        IEnumerable<IBaseTool>? builtInTools = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? llmProvider = null,
        ScriptingHostPorts? hostPorts = null,
        Bindings.RagScriptingBackend? ragBackend = null)
        : this(Microsoft.Extensions.Options.Options.Create(limits ?? new ScriptingLimitsOptions()), loggerFactory, configuration, builtInTools, llmProvider, hostPorts, ragBackend)
    {
    }

    /// <summary>
    /// The interop policy every Orkeon engine runs under. It lives in one place, and is public,
    /// so that a bare <c>new Engine()</c> in a test can be held to the same rules a script really
    /// runs under rather than quietly diverging from them.
    /// <para>
    /// <see cref="ArrayConversionMode.Copy"/> is not nostalgia for Jint's pre-4.14 default. The
    /// DSL publishes typings, and they promise arrays — <c>embed(): Promise&lt;readonly
    /// number[][]&gt;</c> in <c>Typings/context.d.ts</c>. Under Jint's current default,
    /// <see cref="ArrayConversionMode.LiveView"/>, a CLR array reaches the script as a live
    /// wrapper view over the underlying array, and <c>Array.isArray</c> answers false for it.
    /// Copying is what makes the runtime honour the contract the <c>.d.ts</c> files publish;
    /// changing that contract is a decision for the DSL, not a side effect of a version bump.
    /// </para>
    /// <para>
    /// <see cref="ExperimentalFeature.TaskInterop"/> is there for the same reason. The bindings
    /// hand CLR tasks to the script — <c>crew.run()</c> returns
    /// <see cref="System.Threading.Tasks.Task{TResult}"/>, and <c>crew.d.ts</c> declares
    /// <c>run(): Promise&lt;CrewResult&gt;</c>. Without this flag a CLR task is not a thenable,
    /// and <c>await</c> on a non-thenable hands the object straight back: a script read
    /// <c>undefined</c> off a Task where it expected <c>output</c>, and walked on while the crew
    /// was still running. Every procedural example ends on <c>await crew.run()</c>, so the whole
    /// catalogue depended on this.
    /// </para>
    /// </summary>
    /// <param name="options">The options being built for a new engine.</param>
    /// <returns>The same instance, so it can be chained.</returns>
    public static Jint.Options ApplyInteropPolicy(Jint.Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Interop.ArrayConversion = ArrayConversionMode.Copy;
        options.ExperimentalFeatures |= ExperimentalFeature.TaskInterop;
        return options;
    }

    /// <summary>
    /// Creates a fresh, isolated <see cref="Engine"/> with the configured sandbox limits.
    /// </summary>
    public Engine Create()
    {
        var engine = new Engine(opt => ApplyInteropPolicy(opt)
            .LimitMemory(_limits.MemoryLimitBytes)
            .LimitRecursion(_limits.RecursionLimit)
            .TimeoutInterval(_limits.ExecutionTimeout));

        // Register globals exposed to every script. Bindings are added incrementally as
        // builders land (SCR-03..SCR-06).
        AgentBuilderBinding.Register(engine);
        CrewBuilderBinding.Register(engine, _scriptLogger, _llmProvider, _builtInTools, _permissionGate, _deltaSink, _usageSink);
        TaskBuilderBinding.Register(engine);
        ToolBuilderBinding.Register(engine);
        LlmNamespaceBinding.Register(engine, _configuration, _scriptLogger, _llmProvider);
        ToolsNamespaceBinding.Register(engine, _builtInTools ?? Array.Empty<IBaseTool>(), _scriptLogger);
        RagNamespaceBinding.Register(engine, _ragBackend, _scriptLogger);
        ErrorActionBinding.Register(engine);
        StateMachineBinding.Register(engine);
        StateGraphBinding.Register(engine);

        return engine;
    }
}
