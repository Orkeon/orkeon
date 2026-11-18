using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Orkeon.Application.Telemetry;
using Orkeon.Domain.Constants.Http;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Tests.A2A;
using Orkeon.Infrastructure.Tests.Telemetry;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests;

/// <summary>
/// #418: outside MCP too, what Orkeon announces is the version of the release that runs - the activity sources and
/// the meter, the OpenTelemetry resource, the A2A agent card, the User-Agent of the HTTP tools - and no longer a
/// fixed 1.0.0. The public constants keep their value for the callers that compiled them in.
/// </summary>
public sealed class ReleaseVersionAnnouncedTests
{
    /// <summary>The informational version of the assembly under test, build metadata removed.</summary>
    private static string Release
    {
        get
        {
            var informational = typeof(A2AServer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }
    }

    [Fact]
    public void ShouldHaveARelease_ReadAnotherWayThanTheCodeReadsIt()
    {
        Assert.StartsWith(typeof(A2AServer).Assembly.GetName().Version!.ToString(3), Release, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldReportTheRelease_OnEveryActivitySource()
    {
        Assert.Equal(Release, OrkeonActivitySources.Crew.Version);
        Assert.Equal(Release, OrkeonActivitySources.Agent.Version);
        Assert.Equal(Release, OrkeonActivitySources.Task.Version);
        Assert.Equal(Release, OrkeonActivitySources.Llm.Version);
        Assert.Equal(Release, OrkeonActivitySources.Tool.Version);
        Assert.Equal(Release, OrkeonActivitySources.Memory.Version);
        Assert.Equal(Release, OrkeonActivitySources.EventHub.Version);
    }

    [Fact]
    public void ShouldReportTheRelease_OnTheMeter()
    {
        RuntimeHelpers.RunClassConstructor(typeof(OrkeonMeter).TypeHandle);
        var versions = new HashSet<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == OrkeonMeter.Name)
                    versions.Add(instrument.Meter.Version);
            }
        };

        listener.Start();

        Assert.Equal([Release], versions);
    }

    [Fact]
    public void ShouldCarryTheRelease_OnTheOpenTelemetryResource()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true"
        });
        using var provider = services.BuildServiceProvider();
        using var tracer = provider.GetRequiredService<TracerProvider>();

        var attributes = tracer.GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("Orkeon", attributes["service.name"]);
        Assert.Equal(Release, attributes["service.version"]);
    }

    [Fact]
    public void ShouldKeepThePublicConstants_ForTheCallersThatCompiledThemIn()
    {
        Assert.Equal("1.0.0", OrkeonActivitySources.Version);
        Assert.Equal("1.0.0", OrkeonDiagnostics.ServiceVersion);
        Assert.Equal("Orkeon/1.0 (+https://github.com/Orkeon/orkeon)", HttpDefaults.DefaultUserAgent);
    }

    [Fact]
    public void ShouldSendTheRelease_InTheUserAgentOfTheHttpTools()
    {
        Assert.Equal($"Orkeon/{Release} (+https://github.com/Orkeon/orkeon)", Orkeon.Tools.Abstractions.Base.ToolUserAgent.Value);

        // What HttpToolBase does with it: the header parses, and names the product and its version.
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Orkeon.Tools.Abstractions.Base.ToolUserAgent.Value);
        var product = client.DefaultRequestHeaders.UserAgent.First().Product;
        Assert.Equal(new ProductHeaderValue("Orkeon", Release), product);
    }

    private static async Task<string?> CardVersionAsync(string? configured)
    {
        var (server, port) = await A2ALoopback.StartAsync(options =>
        {
            if (configured is not null)
                options.AgentVersion = configured;
            return new A2AServer(options, new StubA2ATaskRouter());
        }, TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            using var card = JsonDocument.Parse(await http.GetStringAsync(
                $"{LoopbackPorts.Host}:{port}/.well-known/agent.json", TestContext.Current.CancellationToken));
            return card.RootElement.GetProperty("version").GetString();
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task ShouldAnnounceTheRelease_InTheAgentCard_WhenNoVersionIsConfigured()
    {
        Assert.Equal("", new A2AOptions().AgentVersion);
        Assert.Equal(Release, await CardVersionAsync(null));
        Assert.Equal(Release, await CardVersionAsync(" "));
    }

    [Fact]
    public async Task ShouldAnnounceTheConfiguredVersion_InTheAgentCard_WhenOneIsSet()
    {
        Assert.Equal("9.8.7-mine", await CardVersionAsync("9.8.7-mine"));
    }
}
