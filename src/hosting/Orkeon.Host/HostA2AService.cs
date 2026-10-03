using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;

namespace Orkeon.Host;

/// <summary>
/// Serves the crews <c>Orkeon:Host:A2A</c> exposes to other agents (GAP-23): checks the section
/// when the host starts — a refusal fails the start with exit 78, before READY=1 and before
/// anything listens — then starts the A2A server, and stops it when the host stops.
/// <para>
/// Registered after the MCP connection and before the chat channel
/// (<see cref="HostServiceRegistration.AddHostLifetimeServices"/>): a task loads a crew, whose
/// MCP tools must be in the registry first, and the server stops after the crew host's drain,
/// so a run in flight during the grace period still answers the peer that asked for it.
/// </para>
/// </summary>
internal sealed partial class HostA2AService : IHostedService
{
    /// <summary>
    /// The keys of the <c>A2A</c> section that C# hosts switch their server with, and that the
    /// daemon replaces with its own section.
    /// </summary>
    private static readonly string[] s_serverKeys = ["A2A:EnableServer", "A2A:Host", "A2A:Port"];

    private readonly OrkeonHostOptions _options;
    private readonly ILogger<HostA2AService> _logger;
    private readonly IConfiguration? _configuration;
    private readonly IA2AServer? _server;
    private readonly A2ASecurityOptions _security;
    private bool _serving;

    /// <summary>Builds the service over the host's options and, when A2A is on, its server.</summary>
    /// <param name="options">The host's options, <c>Orkeon:Host:A2A</c> included.</param>
    /// <param name="logger">The service's logger.</param>
    /// <param name="configuration">The host configuration, read for the A2A keys the daemon refuses.</param>
    /// <param name="server">The A2A server; registered only when <c>Orkeon:Host:A2A:Enabled</c> is set.</param>
    /// <param name="security">The <c>A2A:Security</c> options the server enforces.</param>
    public HostA2AService(
        IOptions<OrkeonHostOptions> options,
        ILogger<HostA2AService> logger,
        IConfiguration? configuration = null,
        IA2AServer? server = null,
        IOptions<A2ASecurityOptions>? security = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration;
        _server = server;
        _security = security?.Value ?? new A2ASecurityOptions();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        RefuseTheA2ASectionsServerKeys();

        var a2a = _options.A2A;
        if (!a2a.Enabled)
            return;

        var exposed = a2a.ExposedCrews(_options.Crews);
        a2a.ValidateListener();

        // A listener the loopback does not contain is reachable by anyone who can route to it:
        // without a credential to check, it would run crews for whoever asks.
        if (!a2a.ListensOnLoopbackOnly && _security.AllowedAuthSchemes.Count == 0 && !_security.RequireMutualTls)
        {
            throw new HostConfigurationException(
                $"{HostA2AOptions.SectionName}:Host is '{a2a.Host}', beyond the loopback interface, and the A2A "
                + "section declares no authentication: set A2A:Security:AllowedAuthSchemes (ApiKey, Bearer) or "
                + "A2A:Security:RequireMutualTls — or listen on http://localhost.");
        }

        if (_server is null)
        {
            throw new InvalidOperationException(
                $"{HostA2AOptions.SectionName} is enabled but no A2A server is registered: the host must be "
                + "built with HostServiceRegistration.AddHostServices.");
        }

        try
        {
            await _server.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // A declared scheme without its validator, mutual TLS without a trust anchor: the
            // configuration, refused like the rest — not a crash to restart on in a loop.
            throw new HostConfigurationException($"The A2A server refused its configuration: {ex.Message}", ex);
        }
        catch (HttpListenerException ex)
        {
            // The system refused the listener (GAP-35): HTTP.sys and a URL nobody reserved for the
            // service's account, a port another process holds. A configuration to fix — exit 78,
            // the step that fixes it in the event log — not a crash the SCM restarted twice in
            // silence. Only here: the HttpListenerException the server swallows on stop is the
            // listener closing, no refusal.
            throw new HostConfigurationException(ListenerRefusal(a2a.ListenerPrefix, ex), ex);
        }

        _serving = true;

        // CA1873: the string.Join stays out of the disabled-logging path.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var crews = string.Join(", ", exposed.Select(c => c.Name));
            LogServing(a2a.Host, a2a.Port, crews);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _serving && _server is not null ? _server.StopAsync(cancellationToken) : Task.CompletedTask;

    /// <summary>
    /// Why the system refused the listener, and what fixes it. Access denied
    /// (<see cref="AccessDenied"/>) is HTTP.sys's answer to an account that is not an administrator
    /// asking for a URL nobody reserved for it — which is what the service account is: the
    /// refusal names the prefix, the account, and the command an administrator runs once.
    /// </summary>
    internal static string ListenerRefusal(string prefix, HttpListenerException ex) =>
        ex.ErrorCode == AccessDenied
            ? $"The system refused the A2A listener {prefix} (access denied): HTTP.sys lets an account that is "
                + $"not an administrator listen only on a URL reserved for it. Reserve it once, as an administrator: "
                + $"netsh http add urlacl url={prefix} user=\"{ProcessAccount()}\" — install-service.ps1 -A2AUrlPrefix {prefix} "
                + "does it when it registers the service."
            : $"The system refused the A2A listener {prefix}: {ex.Message.TrimEnd('.')}. A port under 1024 needs privileges, and a "
                + $"port another process holds cannot be shared: change {HostA2AOptions.SectionName}:Port, or stop the "
                + "process that holds it.";

    /// <summary>ERROR_ACCESS_DENIED — HTTP.sys's refusal of a URL reserved for nobody, or for another account.</summary>
    private const int AccessDenied = 5;

    /// <summary>The account the process runs as, spelled as <c>netsh</c> takes it: <c>NT SERVICE\Orkeon</c> under the SCM.</summary>
    private static string ProcessAccount() =>
        OperatingSystem.IsWindows() ? $"{Environment.UserDomainName}\\{Environment.UserName}" : Environment.UserName;

    /// <summary>
    /// <c>A2A:EnableServer</c>, <c>A2A:Host</c> and <c>A2A:Port</c> switch a C# host's server; the
    /// daemon serves from <c>Orkeon:Host:A2A</c> and reads none of them — written after the C#
    /// hosts' documentation, they would be ignored in silence. Refused whatever the switch says.
    /// </summary>
    private void RefuseTheA2ASectionsServerKeys()
    {
        if (_configuration is null)
            return;

        var written = s_serverKeys.Where(key => _configuration[key] is not null).ToList();
        if (written.Count == 0)
            return;

        throw new HostConfigurationException(
            $"orkeon-host does not read {string.Join(", ", written)}: it serves its crews over A2A from "
            + $"{HostA2AOptions.SectionName} (Enabled, Host, Port, Crews). Move them there — the rest of the A2A "
            + "section (the card's identity, A2A:Security) applies as written.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Serving A2A on {Host}:{Port}: crews {Crews}")]
    private partial void LogServing(string host, int port, string crews);
}
