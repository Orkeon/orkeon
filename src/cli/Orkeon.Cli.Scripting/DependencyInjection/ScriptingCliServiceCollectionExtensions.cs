using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Scripting.Configuration;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Cli.Scripting.Registry;
using Orkeon.Cli.Scripting.Runtime;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Cli.Scripting.DependencyInjection;

/// <summary>
/// Wires scripted CLI commands into a DI container. Registers the loader, the validator,
/// the prompt dispatcher, a CLI-scoped <see cref="JsEngineFactory"/>, the service
/// whitelist, and a deferred-loading <see cref="ScriptCommandRegistry"/> singleton.
/// </summary>
/// <remarks>
/// The registry is registered by its concrete type to avoid clashing with other
/// <c>IInteractiveCommandRegistry</c> singletons in the host (plan Q6). Resolving it is
/// pure wiring (R10.3 / ANT-002): the actual script load runs at the first
/// <see cref="ScriptCommandRegistry.EnsureLoadedAsync"/> call.
/// </remarks>
public static class ScriptingCliServiceCollectionExtensions
{
    /// <summary>
    /// Adds scripted-command discovery to the container with appsettings binding
    /// (section <c>Orkeon:Cli:ScriptCommands</c>) and an optional override callback.
    /// </summary>
    public static IServiceCollection AddScriptCommands(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<ScriptCommandsConfiguration>? configure = null,
        Action<ScriptServiceWhitelist>? configureWhitelist = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind the full configuration record; fall back to defaults when no IConfiguration is supplied.
        if (configuration is not null)
        {
            var section = configuration.GetSection(ScriptCommandsConfiguration.SectionName);
            services.Configure<ScriptCommandsConfiguration>(section.Bind);
        }
        else
        {
            services.AddOptions<ScriptCommandsConfiguration>();
        }
        if (configure is not null)
            services.Configure(configure);

        // Derive the legacy ScriptCommandLoaderOptions from the rich configuration so the
        // loader stays unchanged across phases.
        services.AddOptions<ScriptCommandLoaderOptions>();
        services.AddSingleton<IConfigureOptions<ScriptCommandLoaderOptions>>(sp =>
            new ConfigureNamedOptions<ScriptCommandLoaderOptions>(
                Options.DefaultName,
                target =>
                {
                    var full = sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value;
                    var derived = new ScriptCommandLoaderOptions
                    {
                        Enabled = full.Enabled,
                        Directories = full.Directories,
                        MaxScripts = full.MaxScripts,
                        FailFastOnInvalidScript = full.FailFastOnInvalidScript,
                        ContinueOnConflict = full.ContinueOnConflict,
                        EsbuildTranspile = full.EsbuildTranspile,
                    };
                    typeof(ScriptCommandLoaderOptions).GetProperties()
                        .Where(p => p.CanWrite).ToList()
                        .ForEach(p => p.SetValue(target, p.GetValue(derived)));
                }));

        // Whitelist: defaults + host overrides.
        services.TryAddSingleton<Built>(sp =>
        {
            var builder = DefaultScriptServiceWhitelist.Build();
            configureWhitelist?.Invoke(builder);
            return builder.Build();
        });

        // CLI-dedicated JsEngineFactory built from CliScriptLimitsOptions (spec §8.1).
        // Keyed services would be cleaner, but a single TryAddSingleton works because
        // Orkeon.Cli.Scripting is the only project resolving JsEngineFactory.
        //
        // exp 07 §7.0: pass the built-in tools (and LLM provider) so the `tools.<camelCase>`
        // global is populated — both in command engines (so `.cmd.ts` handlers can call
        // tools directly) and in crew engines launched via ScriptHost/script-host (so `.body()`
        // + ctx.llm + tools work). Without this the namespace exists but is empty.
        services.TryAddSingleton(sp =>
        {
            var cliLimits = sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value.Limits;
            return new JsEngineFactory(
                Options.Create(cliLimits.ToScriptingLimits()),
                sp.GetService<ILoggerFactory>(),
                sp.GetService<IConfiguration>(),
                sp.GetServices<Orkeon.Domain.Tools.IBaseTool>().ToArray(),
                sp.GetService<Orkeon.Domain.SharedKernel.ILlmProvider>(),
                sp.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>());
        });

        // Transpiler: fall back to PassThrough when esbuild isn't configured.
        //
        // NOTE: do NOT call sp.GetServices<IScriptTranspiler>() here. Because this factory is
        // itself the (Try-added) registration for IScriptTranspiler, resolving the IEnumerable
        // re-invokes this very factory → infinite self-recursion that hangs host boot (the DI
        // StackGuard spawns fresh stacks forever). The "prefer a host-registered transpiler"
        // intent is already covered by TryAddSingleton: a host that registers its own
        // IScriptTranspiler before AddScriptCommands wins outright (this factory is never added);
        // one that registers after wins as the last single-service registration. Either way this
        // factory only ever runs as the sole registration, so the lookup could only return itself.
        services.TryAddSingleton<IScriptTranspiler>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value;
            return cfg.EsbuildTranspile
                ? new EsbuildTranspiler()
                : PassThroughTranspiler.Instance;
        });

        // Command-dispatch substrate (design §8). A singleton so the agent that registers
        // (onCommand) and the command that dispatches share one channel/directory/registry,
        // even across different Jint engines. The substrate owns a dedicated in-memory channel
        // (the CLI dispatch bus) to avoid a captive dependency on the host's scoped IAgentChannel.
        services.TryAddSingleton<CommandDispatchService>(sp =>
        {
            var lf = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            IAgentChannel channel = new InMemoryAgentChannel(lf.CreateLogger<InMemoryAgentChannel>());
            return new CommandDispatchService(
                channel,
                new AgentCommandDirectory(),
                new CommandInstanceRegistry(),
                lf.CreateLogger<CommandDispatchService>());
        });

        // The loader needs a live service locator (so ctx.services.get("commands") resolves)
        // and the dispatch service (so async aiguillage + built-ins are wired). Built via a
        // factory because both are constructed, not directly injected.
        services.TryAddSingleton<ScriptCommandLoader>(sp =>
        {
            var whitelist = sp.GetRequiredService<Built>();
            var locator = new ScriptServiceLocator(whitelist, sp);
            return new ScriptCommandLoader(
                sp.GetRequiredService<IFileSystemService>(),
                sp.GetRequiredService<IScriptTranspiler>(),
                sp.GetRequiredService<JsEngineFactory>(),
                sp.GetRequiredService<IOptions<ScriptCommandLoaderOptions>>(),
                new ScriptCommandLoaderDependencies
                {
                    LoggerFactory = sp.GetService<ILoggerFactory>(),
                    Services = locator,
                    Dispatch = sp.GetService<CommandDispatchService>(),
                });
        });

        // R10.3 / ANT-002: pure wiring — resolving the registry must NOT run script discovery,
        // esbuild transpilation, or Jint evaluation (previously several seconds of sync-over-async
        // work executed under the DI singleton-resolution lock). The deferred registry memoises a
        // single load task: the scripted REPL awaits EnsureLoadedAsync in its OnStartAsync, so
        // commands are available before the first prompt and script errors still surface at
        // startup. The loader graph itself is only resolved at that point.
        services.TryAddSingleton<ScriptCommandRegistry>(sp =>
            new ScriptCommandRegistry(ct => sp.GetRequiredService<ScriptCommandLoader>().LoadAndRegisterAsync(ct)));

        // ---- exp 07: the script-host service (cmd → crew bridge, SPEC §6) -------------------
        // Bind crew-directory options (section Orkeon:Cli:ScriptHost) so a host can point the
        // façade at its crews; the ConsoleApp bootstrap also appends mounted crew dirs.
        if (configuration is not null)
        {
            var shSection = configuration.GetSection(ScriptHostFacadeOptions.SectionName);
            services.Configure<ScriptHostFacadeOptions>(shSection.Bind);
        }
        else
        {
            services.AddOptions<ScriptHostFacadeOptions>();
        }

        // The crew runtime: a ScriptHost over the same (tools + LLM) JsEngineFactory, so
        // RunFromFileAsync honours .body()/ctx.llm. Nobody else registers it, so Add (not Try)
        // would clash if the host wired Orkeon.Scripting separately — use TryAdd to defer.
        services.TryAddSingleton(sp => new ScriptHost(
            sp.GetRequiredService<IFileSystemService>(),
            sp.GetRequiredService<IScriptTranspiler>(),
            sp.GetRequiredService<JsEngineFactory>(),
            sp.GetService<ILoggerFactory>()?.CreateLogger<ScriptHost>()));

        services.TryAddSingleton<ScriptHostFacade>(sp => new ScriptHostFacade(
            sp.GetRequiredService<ScriptHost>(),
            sp.GetRequiredService<IFileSystemService>(),
            sp.GetRequiredService<IOptions<ScriptHostFacadeOptions>>(),
            sp.GetService<CommandDispatchService>(),
            sp.GetService<ILoggerFactory>()?.CreateLogger<ScriptHostFacade>()));

        return services;
    }
}
