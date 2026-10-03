using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Infrastructure.AgentCommunication;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-23 — what <c>Orkeon:Host:A2A</c> accepts, and what <see cref="HostA2AService"/> does with
/// it at start: the exposed crews resolved against the declared ones, the listener checked, a
/// listener beyond the loopback refused without a credential to check, the server started at its
/// place in the start order and stopped at its place in the stop order. No socket is opened:
/// <see cref="RecordingA2AServer"/> stands in for the server.
/// </summary>
public sealed class HostA2AConfigurationTests
{
    private static readonly HostedCrewOptions[] s_crews =
    [
        new() { Name = "support", Path = "/crews/support.yaml" },
        new() { Name = "veille", Path = "/crews/veille.yaml" },
    ];

    private static readonly string[] s_exposedInDeclaredSpelling = ["veille", "support"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------
    // The section
    // -------------------------------------------------------------------------

    [Fact]
    public void The_exposed_crews_keep_their_declared_spelling_once_each_in_the_sections_order()
    {
        var exposed = new HostA2AOptions { Crews = ["VEILLE", "support", "veille"] }.ExposedCrews(s_crews);

        Assert.Equal(s_exposedInDeclaredSpelling, exposed.Select(c => c.Name));
    }

    [Theory]
    [InlineData("http://localhost", true)]
    [InlineData("https://LOCALHOST/", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("http://[::1]", true)]
    [InlineData("http://+", false)]
    [InlineData("http://*", false)]
    [InlineData("http://0.0.0.0", false)]
    [InlineData("https://a2a.example.org", false)]
    public void Only_a_loopback_listener_may_serve_without_a_credential(string host, bool loopback)
    {
        var a2a = new HostA2AOptions { Host = host };

        a2a.ValidateListener();
        Assert.Equal(loopback, a2a.ListensOnLoopbackOnly);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("ftp://localhost")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://localhost/a2a")]
    [InlineData("http://")]
    [InlineData("http://my host")]
    [InlineData("")]
    public void A_listener_host_the_server_cannot_take_is_refused_with_the_key(string host)
    {
        var error = Assert.Throws<HostConfigurationException>(() => new HostA2AOptions { Host = host }.ValidateListener());

        Assert.Contains("Orkeon:Host:A2A:Host", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_1_to_65535_is_refused_with_the_key(int port)
    {
        var error = Assert.Throws<HostConfigurationException>(() => new HostA2AOptions { Port = port }.ValidateListener());

        Assert.Contains("Orkeon:Host:A2A:Port", error.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // The service
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_listener_beyond_the_loopback_starts_once_a_credential_is_declared()
    {
        await using var apiKey = new RecordingA2AServer();
        await using var mutualTls = new RecordingA2AServer();

        await Service(Exposing("http://+"), apiKey, new A2ASecurityOptions { AllowedAuthSchemes = { "ApiKey" } }).StartAsync(Ct);
        await Service(Exposing("http://+"), mutualTls, new A2ASecurityOptions { RequireMutualTls = true }).StartAsync(Ct);

        Assert.True(apiKey.Started);
        Assert.True(mutualTls.Started);
    }

    [Fact]
    public async Task A_security_configuration_the_server_refuses_is_a_refused_configuration()
    {
        // Bearer without a validator, mutual TLS without a trust anchor: the server refuses to
        // start with InvalidOperationException, which the daemon reports as what it is — exit 78,
        // not a crash the supervisor restarts in a loop.
        await using var server = new RecordingA2AServer
        {
            StartFailure = new InvalidOperationException("A2A:Security:AllowedAuthSchemes declares 'Bearer' but no token validator is registered"),
        };

        var error = await Assert.ThrowsAsync<HostConfigurationException>(
            () => Service(Exposing("http://localhost"), server).StartAsync(Ct));

        Assert.Contains("declares 'Bearer'", error.Message, StringComparison.Ordinal);
        Assert.Equal(78, HostConfigurationException.ExitCode);
    }

    [Fact]
    public async Task A_listener_the_system_refuses_fails_the_start_naming_the_prefix_and_the_reservation()
    {
        // GAP-35: under the Windows SCM, HTTP.sys refuses to let NT SERVICE\Orkeon listen on a URL
        // nobody reserved for it (ERROR_ACCESS_DENIED, 5). That HttpListenerException left the host
        // as a crash the SCM restarted twice in silence; it is a configuration to fix, and the
        // refusal says how — the exact prefix, and the command an administrator runs once.
        await using var server = new RecordingA2AServer
        {
            StartFailure = new System.Net.HttpListenerException(5, "Access is denied."),
        };

        var error = await Assert.ThrowsAsync<HostConfigurationException>(
            () => Service(Exposing("http://localhost"), server).StartAsync(Ct));

        Assert.Contains("http://localhost:5002/", error.Message, StringComparison.Ordinal);
        Assert.Contains("netsh http add urlacl url=http://localhost:5002/ user=", error.Message, StringComparison.Ordinal);
        Assert.Contains("-A2AUrlPrefix", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_port_the_system_will_not_give_fails_the_start_naming_the_prefix()
    {
        // Any other refusal of the listener — a port another process holds, a port under 1024
        // without the privilege — is the configuration too, named, with what to change.
        await using var server = new RecordingA2AServer
        {
            StartFailure = new System.Net.HttpListenerException(183, "Cannot create a file when that file already exists."),
        };

        var error = await Assert.ThrowsAsync<HostConfigurationException>(
            () => Service(Exposing("http://localhost"), server).StartAsync(Ct));

        Assert.Contains("http://localhost:5002/", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Host:A2A:Port", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("netsh", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_server_it_started_stops_with_the_host_and_a_disabled_section_touches_nothing()
    {
        await using var served = new RecordingA2AServer();
        var service = Service(Exposing("http://localhost"), served);
        await service.StartAsync(Ct);
        await service.StopAsync(Ct);

        await using var idle = new RecordingA2AServer();
        var disabled = Service(new HostA2AOptions { Enabled = false, Crews = ["nowhere"] }, idle);
        await disabled.StartAsync(Ct);
        await disabled.StopAsync(Ct);

        Assert.True(served.Started && served.Stopped);
        Assert.False(idle.Started || idle.Stopped);
    }

    [Fact]
    public async Task The_A2A_sections_server_keys_are_refused_even_with_the_hosts_section_off()
    {
        // Written after the C# hosts' documentation, A2A:EnableServer would otherwise enable
        // nothing and say nothing.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["A2A:EnableServer"] = "true" })
            .Build();

        var error = await Assert.ThrowsAsync<HostConfigurationException>(
            () => Service(new HostA2AOptions(), configuration: configuration).StartAsync(Ct));

        Assert.Contains("A2A:EnableServer", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Host:A2A", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_A2A_server_starts_after_the_MCP_servers_and_before_the_chat_channel()
    {
        // After MCP: a task loads a crew, whose MCP tools must be registered first. Before the
        // channel and the crew host: stopped after the drain, so a run in flight still answers
        // the peer that asked.
        var services = new ServiceCollection();

        services.AddHostLifetimeServices();

        Assert.Equal(
            [typeof(McpConnectionService), typeof(HostA2AService), typeof(Gateway.ChatChannelService), typeof(CrewHostService)],
            services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType));
    }

    private static HostA2AOptions Exposing(string host) =>
        new() { Enabled = true, Host = host, Crews = ["veille"] };

    private static HostA2AService Service(
        HostA2AOptions a2a,
        IA2AServer? server = null,
        A2ASecurityOptions? security = null,
        IConfiguration? configuration = null) =>
        new(
            Options.Create(new OrkeonHostOptions { Crews = s_crews, A2A = a2a }),
            NullLogger<HostA2AService>.Instance,
            configuration,
            server,
            security is null ? null : Options.Create(security));
}
