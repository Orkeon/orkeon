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
/// <c>--list-tools</c> so the three see the same tool surface. A server that cannot be
/// connected — a command that does not exist, an endpoint that does not answer, a server that
/// never completes its handshake — costs one error line naming it, on the log and on stderr,
/// and nothing else: the run goes on, and a crew that names one of that server's tools fails
/// at load under <c>StrictTools</c> with the ordinary "unknown tool" line rather than here.
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
    /// Connects every declared server and registers its tools; a no-op on a host built without
    /// the section. Never throws for a server: see the class summary.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A server that cannot start, answer or complete its handshake fails in any exception type the transports and the process API produce; every one of them is the same event for the run - that server is unavailable - and is reported as such rather than ending the run.")]
    public static async Task ConnectConfiguredServersAsync(IHost host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);

        var provider = host.Services.GetService<McpToolProvider>();
        if (provider is null)
            return;

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orkeon.Hosting.McpStartup");
        var servers = host.Services.GetRequiredService<IOptions<McpOptions>>().Value.Servers;

        foreach (var (serverId, config) in servers)
        {
            ct.ThrowIfCancellationRequested();
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
            guard.CancelAfter(ConnectTimeout);
            try
            {
                await provider.ConnectServerAsync(serverId, config, guard.Token).ConfigureAwait(false);
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

    private static void Report(ILogger logger, string serverId, string reason)
    {
        LogMcpServerUnavailable(logger, serverId, reason);
        // stderr is the guarantee channel: the host may keep its logger below Error.
        Console.Error.WriteLine($"MCP server '{serverId}' could not be connected: {reason}");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP server '{ServerId}' could not be connected: {Reason}. Its tools are absent from this run.")]
    private static partial void LogMcpServerUnavailable(ILogger logger, string serverId, string reason);
}
