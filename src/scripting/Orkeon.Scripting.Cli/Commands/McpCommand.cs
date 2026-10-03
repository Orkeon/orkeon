using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Parsed options of <c>orkeon mcp serve</c>.</summary>
[Verb("serve", HelpText = "Serve this host's tools to an MCP client over stdio: JSON-RPC in on stdin, out on stdout, one message per line.")]
internal sealed class McpServeCommandOptions
{
    /// <summary>Path to appsettings.json.</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain, like `orkeon run`).")]
    public string? SettingsPath { get; set; }

    /// <summary>The tools to serve; every tool of the host when none is named.</summary>
    [Option("tools", Required = false, Separator = ',',
        HelpText = "Comma-separated names of the tools to serve (default: every tool of the host). A name the host does not have refuses the start.")]
    public IEnumerable<string> Tools { get; set; } = [];

    /// <summary>Test seam: overrides the current directory the host is anchored at.</summary>
    internal string? WorkingDirectoryOverride { get; set; }
}

/// <summary>
/// <c>orkeon mcp serve [--settings &lt;file&gt;] [--tools a,b,…]</c> — serves the tools of an
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
/// Until this verb no shipped binary started the server: <c>MCP:EnableServer</c> registered it for
/// nobody. The verb is the switch now, and the key is refused.
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
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var parser = new Parser(s =>
        {
            // The help too: on this verb stdout carries the protocol and nothing else.
            s.HelpWriter = Console.Error;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments(args, typeof(McpServeCommandOptions))
            .MapResult(
                (McpServeCommandOptions o) => ServeAsync(o),
                errors => Task.FromResult(errors.IsHelp() || errors.IsVersion() ? Program.ExitOk : Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    /// <summary>Serves until the client closes stdin.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: an unexpected failure is reported in one line on stderr, the stream a client records as its server's log, with a runtime-error exit code instead of a crash.")]
    internal static async Task<int> ServeAsync(McpServeCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Started by another serve — its settings declare `orkeon mcp serve` under MCP:Servers
        // (GAP-35): each server started another before answering, until the first gave up after
        // 30 s. The refusal is one line on stderr, which the parent joins to its connection error.
        if (RunnerEnvironment.StartedByMcpServe)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon mcp serve: refused to start: another `orkeon mcp serve` started this one "
                + $"({RunnerEnvironment.McpServeVariable} is set), its settings declaring it under MCP:Servers. "
                + "Remove that entry, or declare both servers in the client instead.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        // Marked before the settings' servers are connected, so every process this serve starts
        // inherits the marker; cleared when it ends, so the process is left as it was found.
        Environment.SetEnvironmentVariable(RunnerEnvironment.McpServeVariable, "1");
        try
        {
            using var host = await BuildHostAsync(options).ConfigureAwait(false);
            if (host is null)
                return Program.ExitScriptError;

            if (!await ServeOnlyAsync(host.Services.GetRequiredService<IToolRegistry>(), options.Tools).ConfigureAwait(false))
                return Program.ExitScriptError;

            await host.Services.GetRequiredService<McpServer>().RunStdioAsync().ConfigureAwait(false);
            return Program.ExitOk;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon mcp serve: unexpected error [{ex.GetType().FullName}]: {ex.Message}").ConfigureAwait(false);
            if (RunnerEnvironment.DebugDiagnostics)
                await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerEnvironment.McpServeVariable, null);
        }
    }

    /// <summary>
    /// The host <c>--list-tools</c> reports, plus the server: what <c>orkeon run</c> adds to the
    /// runner host — <c>semantic_search</c>, the ONNX reranker —, and <c>AddOrkeonMcpServer</c>.
    /// Null when the settings are refused — the removed <c>MCP:EnableServer</c> among them —; the
    /// line that says why is already on stderr.
    /// </summary>
    private static async Task<IHost?> BuildHostAsync(McpServeCommandOptions options)
    {
        var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, cwd);
        if (settingsPath != null)
            await Console.Error.WriteLineAsync($"Using settings: {settingsPath}").ConfigureAwait(false);

        return await RunnerExecution.BuildToolHostAsync(
            settingsPath,
            cwd,
            cliMounts: [],
            mountIds: [],
            allowExternalMounts: false,
            (context, services) =>
            {
                services.AddSemanticSearchTool();
                RunCommand.AddCliRagServices(services);
                services.AddOrkeonMcpServer(context.Configuration);
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>--tools</c>: every name must be a tool of the host — one that is not refuses the start,
    /// named — and every other tool leaves the registry, which this host keeps for the server alone.
    /// </summary>
    private static async Task<bool> ServeOnlyAsync(IToolRegistry registry, IEnumerable<string> names)
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
            await Console.Error.WriteLineAsync(wanted.Count == 0
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
