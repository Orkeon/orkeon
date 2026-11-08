using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Parsed options of <c>orkeon mcp serve</c>.</summary>
[Verb("serve", HelpText = "Serve this host's tools to an MCP client over stdio: JSON-RPC in on stdin, out on stdout, one message per line.")]
internal sealed class McpServeCommandOptions : CliOptions
{
    /// <summary>Path to appsettings.json.</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain, like `orkeon run`).")]
    public string? SettingsPath { get; set; }

    /// <summary>The tools to serve; every tool of the host when none is named.</summary>
    [Option("tools", Required = false, Separator = ',',
        HelpText = "Comma-separated names of the tools to serve (default: every tool of the host). A name the host does not have refuses the start.")]
    public IEnumerable<string> Tools { get; set; } = [];

    /// <summary>How many requests are served at once; the settings' <c>MCP:Server:MaxConcurrency</c> (4) when absent.</summary>
    [Option("max-concurrency", Required = false,
        HelpText = "How many requests are served at once, at least 1 (default 4, or MCP:Server:MaxConcurrency); the others wait their turn.")]
    public int? MaxConcurrency { get; set; }

    /// <summary>Serves <c>/output</c> as read-only MCP resources.</summary>
    [Option("expose-output", Required = false,
        HelpText = "Serve /output as read-only MCP resources (orkeon:///output/<path>), through the host's virtual file system. /workspace is never served.")]
    public bool ExposeOutput { get; set; }

    /// <summary>The crews to serve as <c>run_crew_&lt;name&gt;</c> tools.</summary>
    [Option("crew", Required = false,
        HelpText = "Serve a crew as the tool run_crew_<name> (its name, lower case), its input the {placeholders} of its tasks, its result the crew's final output, with progress when the client sends a progressToken; "
                   + "its tasks are also offered as prompts. Several crews go space-separated after ONE --crew (the flag cannot be repeated). Budget, guardian and permission gate are those of `orkeon run`.")]
    public IEnumerable<string> Crews { get; set; } = [];

    /// <summary>Lets a crew outside the working directory be served.</summary>
    [Option("allow-external-mounts", Required = false, Default = false,
        HelpText = "Allow a --crew whose folder is outside the working directory, as `orkeon run --allow-external-mounts` does. Can also be enabled via ORKEON_ALLOW_EXTERNAL_MOUNTS=1.")]
    public bool AllowExternalMounts { get; set; }

    /// <summary>Test seam: services added to the host of every <c>--crew</c>.</summary>
    internal Action<Microsoft.Extensions.Hosting.HostBuilderContext, IServiceCollection>? ConfigureCrewServices { get; set; }

    /// <summary>Test seam: overrides the current directory the host is anchored at.</summary>
    internal string? WorkingDirectoryOverride { get; set; }
}

/// <summary>
/// <c>orkeon mcp serve [--settings &lt;file&gt;] [--tools a,b,…] [--max-concurrency n] [--expose-output] [--crew a.yaml b.yaml]</c> — serves the tools of an
/// Orkeon host to an MCP client over stdio (GAP-24), the transport by which Claude Desktop and the
/// editors launch a local server.
/// <para>
/// The host is the one <c>orkeon run --list-tools</c> reports: the settings resolved from the
/// current directory, their mounts, the built-in tools, the MCP servers they declare. Its registry
/// is what the server serves, <c>human_input</c> aside — that tool answers for the operator of a
/// run, and the client of an MCP server has a human of its own. Each call crosses the invocation
/// point of the agent turns (<see cref="McpServer"/>). Stdout belongs to the protocol: the logs,
/// the help and every diagnostic go to stderr, where a client records its server's output.
/// </para>
/// <para>
/// This verb is what starts the server: no setting does.
/// </para>
/// <para>
/// A serve never runs under another one (GAP-35): it marks its environment
/// (<see cref="RunnerEnvironment.McpServeVariable"/>) before it connects the MCP servers of its
/// settings, and one started by a process so marked refuses at once — settings declaring the verb
/// under their own <c>MCP:Servers</c> made each server start another before answering.
/// </para>
/// </summary>
internal static class McpCommand
{
    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>mcp</c>) and dispatches.</summary>
    public static async Task<int> DispatchAsync(IRunnerConsole console, string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return await CliGrammar.DispatchAsync(console, "mcp", args, [typeof(McpServeCommandOptions)], (console, options) => ServeAsync(console, (McpServeCommandOptions)options), helpOnStderr: true).ConfigureAwait(false);
    }

    /// <summary>Serves until the client closes stdin.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: an unexpected failure is reported in one line on stderr, the stream a client records as its server's log, with a runtime-error exit code instead of a crash.")]
    internal static async Task<int> ServeAsync(IRunnerConsole console, McpServeCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);

        // Started by another serve — its settings declare `orkeon mcp serve` under MCP:Servers
        // (GAP-35): each server started another before answering, until the first gave up after
        // 30 s. The refusal is one line on stderr, which the parent joins to its connection error.
        if (RunnerEnvironment.StartedByMcpServe)
        {
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon mcp serve: refused to start: another `orkeon mcp serve` started this one "
                + $"({RunnerEnvironment.McpServeVariable} is set), its settings declaring it under MCP:Servers. "
                + "Remove that entry, or declare both servers in the client instead.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        if (options.MaxConcurrency is < 1)
        {
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon mcp serve: --max-concurrency must be at least 1, got {options.MaxConcurrency}.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        // Marked before the settings' servers are connected, so every process this serve starts
        // inherits the marker; cleared when it ends, so the process is left as it was found.
        Environment.SetEnvironmentVariable(RunnerEnvironment.McpServeVariable, "1");
        try
        {
            // The crews first: a crew that does not load, or a name given twice, refuses the start before the tools host is built.
            using var crews = await OpenCrewsAsync(console, options).ConfigureAwait(false);
            if (options.Crews.Any() && crews is null)
                return Program.ExitScriptError;

            using var host = await BuildHostAsync(console, options, crews).ConfigureAwait(false);
            if (host is null)
                return Program.ExitScriptError;

            if (!await ServeOnlyAsync(console.ErrorOutput, host.Services.GetRequiredService<IToolRegistry>(), options.Tools).ConfigureAwait(false))
                return Program.ExitScriptError;

            if (crews is not null && !await CrewNamesAreFreeAsync(console.ErrorOutput, host.Services.GetRequiredService<IToolRegistry>(), crews).ConfigureAwait(false))
                return Program.ExitScriptError;

            if (options.ExposeOutput && !host.Services.GetRequiredService<IFileSystemService>().GetAvailableMounts()
                    .Any(mount => string.Equals(mount.VirtualPath, "/output", StringComparison.Ordinal)))
            {
                await console.ErrorOutput.WriteLineAsync(
                    "orkeon mcp serve: warning: --expose-output, but no /output mount is declared in the settings: "
                    + "`resources/list` will refuse until one is (`Orkeon:FileSystem:Mounts`).").ConfigureAwait(false);
            }

            await host.Services.GetRequiredService<McpServer>().RunStdioAsync(console.Input, console.Output).ConfigureAwait(false);
            return Program.ExitOk;
        }
        catch (Exception ex)
        {
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon mcp serve: unexpected error [{ex.GetType().FullName}]: {ex.Message}").ConfigureAwait(false);
            if (RunnerEnvironment.DebugDiagnostics)
                await console.ErrorOutput.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerEnvironment.McpServeVariable, null);
        }
    }

    /// <summary>Opens the <c>--crew</c> crews, each through the entry point <c>orkeon run</c> loads a crew with; null when none is named or one refuses (the reason is on stderr).</summary>
    private static async Task<McpCrewToolHost?> OpenCrewsAsync(IRunnerConsole console, McpServeCommandOptions options)
    {
        var paths = options.Crews.ToList();
        if (paths.Count == 0)
            return null;

        var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
        return await McpCrewToolHost.OpenAsync(
            paths,
            new McpCrewHostOptions
            {
                SettingsPath = options.SettingsPath,
                WorkingDirectory = cwd,
                AllowExternalMounts = options.AllowExternalMounts,
                Console = console,
                ConfigureServices = options.ConfigureCrewServices,
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>A <c>run_crew_*</c> name a tool of the host already holds refuses the start, named: the client could not tell them apart.</summary>
    private static async Task<bool> CrewNamesAreFreeAsync(TextWriter error, IToolRegistry registry, McpCrewToolHost crews)
    {
        var held = (await registry.GetAllToolsAsync().ConfigureAwait(false)).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var clash = crews.Tools.Select(t => t.Name).FirstOrDefault(held.Contains);
        if (clash is null)
            return true;

        await error.WriteLineAsync($"orkeon mcp serve: the tool name '{clash}' of a --crew is already held by a tool of the host.").ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// The host <c>--list-tools</c> reports, plus the server: what <c>orkeon run</c> adds to the
    /// runner host — <c>semantic_search</c>, the ONNX reranker —, and <c>AddOrkeonMcpServer</c>.
    /// Null when the settings are refused; the
    /// line that says why is already on stderr.
    /// </summary>
    private static async Task<IHost?> BuildHostAsync(IRunnerConsole console, McpServeCommandOptions options, McpCrewToolHost? crews)
    {
        var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, cwd, console.ErrorOutput);
        if (settingsPath != null)
            await console.ErrorOutput.WriteLineAsync($"Using settings: {settingsPath}").ConfigureAwait(false);

        return await RunnerExecution.BuildToolHostAsync(
            settingsPath,
            cwd,
            // No --mount, no --mount-id, no crew: a root declared twice has one remedy, in the settings.
            new ToolHostMounts(CliMounts: [], MountIds: [], AllowExternalMounts: false, Offered: OfferedMountSelectors.None),
            (context, services) =>
            {
                services.AddSingleton(console);
                services.AddSemanticSearchTool();
                RunCommand.AddCliRagServices(services);
                if (crews is not null)
                    services.AddSingleton<IMcpServerExtension>(crews);
                services.AddOrkeonMcpServer(context.Configuration);
                // The verb's options win over MCP:Server, which they extend.
                services.PostConfigure<McpServerOptions>(server =>
                {
                    if (options.MaxConcurrency is int bound)
                        server.MaxConcurrency = bound;
                    if (options.ExposeOutput)
                        server.ExposeOutput = true;
                });
            },
            console.ErrorOutput,
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>--tools</c>: every name must be a tool of the host — one that is not refuses the start,
    /// named — and every other tool leaves the registry, which this host keeps for the server alone.
    /// </summary>
    private static async Task<bool> ServeOnlyAsync(TextWriter error, IToolRegistry registry, IEnumerable<string> names)
    {
        var requested = names.ToList();
        if (requested.Count == 0)
            return true;

        var wanted = requested
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tools = await registry.GetAllToolsAsync().ConfigureAwait(false);
        var unknown = wanted
            .Where(name => !tools.Any(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (wanted.Count == 0 || unknown.Count > 0)
        {
            await error.WriteLineAsync(wanted.Count == 0
                    ? "orkeon mcp serve: --tools names no tool."
                    : $"orkeon mcp serve: --tools names what this host does not have: {string.Join(", ", unknown)}. "
                      + "`orkeon run --list-tools`, with the same settings, lists its tools.")
                .ConfigureAwait(false);
            return false;
        }

        foreach (var tool in tools.Where(tool => !wanted.Contains(tool.Name)))
            await registry.UnregisterToolAsync(tool.Name).ConfigureAwait(false);

        return true;
    }
}
