using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Constants.Configuration;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Hosting;

/// <summary>
/// Connects the MCP servers the settings declare, before a crew loads (STUDIO-21).
/// <para>
/// The runners never start the host, so no hosted service could do this; it is an explicit
/// step, like the telemetry activation, called by the kickoff, by <c>--validate</c> and by
/// <c>--list-tools</c> so the three see the same tool surface — and by <c>orkeon-host</c>'s
/// connection service, once at startup, before any message can load a crew. A server that cannot be
/// connected — a command that does not exist, an endpoint that does not answer, a server that
/// never completes its handshake — costs one error line naming it, on the log and on stderr,
/// and nothing else: the run goes on, and a crew that names one of that server's tools fails
/// at load under <c>StrictTools</c> with the ordinary "unknown tool" line rather than here.
/// A server tool named like a tool already registered is refused, never substituted: the
/// registered tool keeps the name, and one error line names the server and the tool.
/// </para>
/// </summary>
internal static partial class McpStartup
{
    /// <summary>
    /// How long one server gets to start and answer the handshake. A server that hangs must
    /// not hang the run: past this, it is reported like a server that failed.
    /// </summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether the settings declare at least one MCP server and do not switch MCP off. This is
    /// the one gate: without it the section is bound by nobody, and a run is exactly what it
    /// was before MCP reached the runners.
    /// </summary>
    public static bool IsConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationKeys.McpSection);
        if (!section.Exists() || !section.GetValue("Enabled", defaultValue: true))
            return false;

        var servers = section.GetSection("Servers");
        return servers.Exists() && servers.GetChildren().Any();
    }

    /// <summary>
    /// <c>MCP:EnableServer</c> registered a server no runner ever started (GAP-24). The key is
    /// gone, and a settings file that still writes it — servers declared or not, <c>true</c> or
    /// <c>false</c> — fails the host here, naming the verb that serves instead, rather than being
    /// ignored the way it always was.
    /// </summary>
    /// <exception cref="InvalidOperationException">The settings carry <c>MCP:EnableServer</c>.</exception>
    public static void RefuseServerSwitch(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetSection(ConfigurationKeys.McpSection).GetSection("EnableServer").Exists())
            return;

        throw new InvalidOperationException(
            $"{ConfigurationKeys.McpSection}:EnableServer is not a setting any more: no runner starts an MCP server from its settings. " +
            "`orkeon mcp serve` serves this host's tools to an MCP client over stdio. Remove the key.");
    }

    /// <summary>
    /// Connects every declared server and registers its tools; a no-op on a host built without
    /// the section. Never throws for a server: see the class summary.
    /// </summary>
    public static Task ConnectConfiguredServersAsync(IHost host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        return ConnectConfiguredServersAsync(host.Services, ct);
    }

    /// <summary>
    /// The same step over a service provider — the form <c>orkeon-host</c> calls from its
    /// hosted service, once, before the first message can load a crew (GAP-11).
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A server that cannot start, answer or complete its handshake fails in any exception type the transports and the process API produce; every one of them is the same event for the run - that server is unavailable - and is reported as such rather than ending the run.")]
    public static async Task ConnectConfiguredServersAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        var provider = services.GetService<McpToolProvider>();
        if (provider is null)
            return;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Orkeon.Hosting.McpStartup");
        var servers = services.GetRequiredService<IOptions<McpOptions>>().Value.Servers;

        foreach (var (serverId, config) in servers)
        {
            ct.ThrowIfCancellationRequested();
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
            guard.CancelAfter(ConnectTimeout);
            try
            {
                await provider.ConnectServerAsync(serverId, config, guard.Token).ConfigureAwait(false);
                ReportCollisions(provider, serverId);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Report(logger, serverId, $"no answer within {ConnectTimeout.TotalSeconds:0} s");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Report(logger, serverId, ex.Message);
            }
        }
    }

    /// <summary>
    /// A server tool whose name is already held — by a built-in tool or another server's — is
    /// refused by the registry and named on the log by <see cref="McpToolProvider"/>; stderr
    /// carries the same line, for the same reason as <see cref="Report"/>.
    /// </summary>
    private static void ReportCollisions(McpToolProvider provider, string serverId)
    {
        var status = provider.GetServerStatuses().FirstOrDefault(s => s.ServerId == serverId);
        foreach (var name in status?.RejectedToolNames ?? [])
        {
            Console.Error.WriteLine(
                $"The tool '{name}' of MCP server '{serverId}' collides with an already-registered tool and was not registered.");
        }
    }

    private static void Report(ILogger logger, string serverId, string reason)
    {
        LogMcpServerUnavailable(logger, serverId, reason);
        // stderr is the guarantee channel: the host may keep its logger below Error.
        Console.Error.WriteLine($"MCP server '{serverId}' could not be connected: {reason}");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP server '{ServerId}' could not be connected: {Reason}. Its tools are absent from this run.")]
    private static partial void LogMcpServerUnavailable(ILogger logger, string serverId, string reason);
}
