using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// #417: <c>tools/list</c> is read tool by tool. An entry that cannot be read is set aside, warned about once
/// under the name the operator gave the server, and never takes the listing down.
/// </summary>
public sealed class McpClientToolListingTests
{
    private static async Task<(McpClient Client, MockMcpTransport Transport, RecordingLogger Log)> ConnectAsync(string listing)
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(req => req.Method switch
        {
            "initialize" => new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonSerializer.SerializeToElement(new McpInitializeResult
                {
                    ProtocolVersion = "2025-06-18",
                    Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo() },
                    ServerInfo = new McpServerInfo { Name = "self-reported", Version = "0" }
                })
            },
            "tools/list" => new JsonRpcResponse { Id = req.Id, Result = JsonElement.Parse(listing) },
            _ => new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") }
        });
        // The factory holds nothing to release: it hands out its one recording logger.
        using var factory = new RecordingLoggerFactory();
        var log = factory.Logger;
        var client = new McpClient(transport, new Logger<McpClient>(factory)) { ServerLabel = "configured-id" };
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        return (client, transport, log);
    }

    private static List<string> Warnings(RecordingLogger log) =>
        log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();

    [Fact]
    public async Task ShouldWarnOncePerDefinition_WhenTheSameBadEntryIsListedAgain()
    {
        var (client, transport, log) = await ConnectAsync("""
            {"tools":[{"name":"alpha","description":"ok"},{"name":"gamma","description":5},{"name":"","description":"nameless"},null]}
            """);
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 3; listing++)
        {
            var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["alpha"], tools.Select(t => t.Name));
        }

        var warnings = Warnings(log);
        Assert.Equal(3, warnings.Count); // gamma, the nameless one, the null entry: once each over three listings
        Assert.Single(warnings, w => w.Contains("'gamma'", StringComparison.Ordinal));
        // The server is named as the operator configured it, not as it names itself.
        Assert.All(warnings, w => Assert.Contains("'configured-id'", w, StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("self-reported", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"tools":null}""")]
    [InlineData("""{"tools":[]}""")]
    [InlineData("{}")]
    public async Task ShouldListNoTool_WhenTheServerListsNone(string listing)
    {
        var (client, transport, log) = await ConnectAsync(listing);
        await using var _ = client;
        await using var __ = transport;

        Assert.Empty(await client.ListToolsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(Warnings(log));
    }
}
