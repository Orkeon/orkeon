using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Parsed options of <c>orkeon bridge serve</c>.</summary>
[Verb("serve", HelpText = "Run, for an Orkeon on another machine (a container), the tools Orkeon:Bridge selects, over HTTP, until Ctrl+C.")]
internal sealed class BridgeServeCommandOptions : CliOptions
{
    /// <summary>Path to appsettings.json.</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain, like `orkeon run`). Its Orkeon:Bridge section says where the bridge listens and which tools it runs.")]
    public string? SettingsPath { get; set; }

    /// <summary>Test seam: reads an environment variable in place of the process environment.</summary>
    internal Func<string, string?>? ReadEnvironment { get; set; }

    /// <summary>Test seam: told once the bridge listens.</summary>
    internal Action<ToolBridgeServer>? Started { get; set; }

    /// <summary>Test seam: overrides the current directory the host is anchored at.</summary>
    internal string? WorkingDirectoryOverride { get; set; }
}

/// <summary>
/// <c>orkeon bridge serve [--settings &lt;file&gt;]</c> — the serving side of the tool bridge (#445, ADR-019): an
/// Orkeon on the user's machine runs, for an Orkeon elsewhere (a container), the built-in tools both sides select.
/// <para>
/// The host is the one <c>orkeon mcp serve</c> builds (the settings resolved from the current directory, their
/// mounts, the built-in tools); <see cref="ToolBridgeServer"/> listens on <c>Orkeon:Bridge:Prefix</c> (loopback by
/// default), checks the token read from the variable <c>Orkeon:Bridge:TokenEnvVar</c> names, and runs each call raw.
/// A refused setting, a missing token, a name that selects no tool or a prefix it cannot listen on is one line on
/// stderr and exit 1. Ctrl+C or SIGTERM stops it: the calls in flight are cancelled and the runs' browsers closed.
/// </para>
/// <para>This verb is what starts the bridge: no setting does.</para>
/// </summary>
internal static class BridgeCommand
{
    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>bridge</c>) and dispatches.</summary>
    public static async Task<int> DispatchAsync(IRunnerConsole console, string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return await CliGrammar.DispatchAsync(console, "bridge", args, [typeof(BridgeServeCommandOptions)], (console, options) => ServeAsync(console, (BridgeServeCommandOptions)options)).ConfigureAwait(false);
    }

    /// <summary>Serves until Ctrl+C or SIGTERM.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: an unexpected failure is reported in one line on stderr with a runtime-error exit code instead of a crash.")]
    internal static async Task<int> ServeAsync(IRunnerConsole console, BridgeServeCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);

        using var stop = new CancellationTokenSource();
        using var interrupt = console.OnInterrupt(() =>
        {
            stop.Cancel();
            return true;
        });
        using var terminate = RegisterTermination(stop);
        try
        {
            using var host = await BuildHostAsync(console, options, stop.Token).ConfigureAwait(false);
            if (host is null)
                return Program.ExitScriptError;

            var services = host.Services;
            var loggerFactory = services.GetService<ILoggerFactory>();
            var server = new ToolBridgeServer(
                services.GetRequiredService<IToolRegistry>(),
                services.GetRequiredService<IOptions<ToolBridgeServerOptions>>().Value,
                options.ReadEnvironment,
                services.GetService<IAuditLogger>(),
                loggerFactory?.CreateLogger("Orkeon.Hosting.ToolBridge"),
                services.GetService<IFileSystemScope>(),
                services.GetService<FileSystemRegistry>(),
                services.GetService<TimeProvider>());
            await using var serverLifetime = server.ConfigureAwait(false);
            await server.StartAsync(stop.Token).ConfigureAwait(false);

            await console.Output.WriteLineAsync($"orkeon bridge serve: listening on {server.Prefix}").ConfigureAwait(false);
            await console.Output.WriteLineAsync(
                "orkeon bridge serve: serving " + (server.ServedTools.Count == 0
                    ? "no tool (Orkeon:Bridge:Allow names none)"
                    : string.Join(", ", server.ServedTools))).ConfigureAwait(false);
            foreach (var warning in server.Warnings)
                await console.ErrorOutput.WriteLineAsync($"orkeon bridge serve: warning: {warning}").ConfigureAwait(false);
            await console.Output.WriteLineAsync("orkeon bridge serve: Ctrl+C stops it.").ConfigureAwait(false);
            options.Started?.Invoke(server);

            try
            {
                await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Ctrl+C or SIGTERM: the server stops below, its calls cancelled and its runs ended.
            }

            await server.StopAsync().ConfigureAwait(false);
            await console.Output.WriteLineAsync("orkeon bridge serve: stopped.").ConfigureAwait(false);
            return Program.ExitOk;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return Program.ExitOk;
        }
        catch (RunnerSettingsException ex)
        {
            if (RunnerEnvironment.DebugDiagnostics)
                await console.ErrorOutput.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            await console.ErrorOutput.WriteLineAsync($"orkeon bridge serve: {ex.Message}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (Exception ex)
        {
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon bridge serve: unexpected error [{ex.GetType().FullName}]: {ex.Message}").ConfigureAwait(false);
            if (RunnerEnvironment.DebugDiagnostics)
                await console.ErrorOutput.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
    }

    /// <summary>Cancels <paramref name="stop"/> on SIGTERM; nothing where the platform has none.</summary>
    private static System.Runtime.InteropServices.PosixSignalRegistration? RegisterTermination(CancellationTokenSource stop)
    {
        try
        {
            return System.Runtime.InteropServices.PosixSignalRegistration.Create(
                System.Runtime.InteropServices.PosixSignal.SIGTERM,
                context =>
                {
                    context.Cancel = true;
                    stop.Cancel();
                });
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The host <c>orkeon mcp serve</c> builds, without the MCP server: the settings' mounts, the built-in tools,
    /// <c>semantic_search</c> and the RAG services. Null when the settings are refused; the line that says why is on stderr.
    /// </summary>
    private static async Task<IHost?> BuildHostAsync(IRunnerConsole console, BridgeServeCommandOptions options, CancellationToken ct)
    {
        var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, cwd, console.ErrorOutput);
        if (settingsPath != null)
            await console.ErrorOutput.WriteLineAsync($"Using settings: {settingsPath}").ConfigureAwait(false);

        return await RunnerExecution.BuildToolHostAsync(
            settingsPath,
            cwd,
            new ToolHostMounts(CliMounts: [], MountIds: [], AllowExternalMounts: false, Offered: OfferedMountSelectors.None),
            (_, services) =>
            {
                services.AddSingleton(console);
                services.AddSemanticSearchTool();
                RunCommand.AddCliRagServices(services);
                ToolBridgeStartup.RegisterServing(services);
            },
            console.ErrorOutput,
            ct).ConfigureAwait(false);
    }
}
