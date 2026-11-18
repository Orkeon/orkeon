using System.Reflection;
using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// #416: the MCP server and the MCP client introduce themselves with the version of the release they are
/// built from, in both eras of the protocol, unless <c>MCP:Server:Version</c> sets the server's.
/// </summary>
public sealed class McpAnnouncedVersionTests
{
    /// <summary>The informational version of the assembly under test, build metadata removed.</summary>
    private static string Release
    {
        get
        {
            var informational = typeof(McpServer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }
    }

    private static McpServer Server(McpServerOptions options) =>
        new(new MockToolRegistry(), ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), options);

    private static async Task<string?> LegacyServerVersionAsync(McpServerOptions options)
    {
        var response = await Server(options).ProcessRequestAsync(new JsonRpcRequest
        {
            Method = "initialize",
            Id = JsonSerializer.SerializeToElement(1),
            Params = JsonElement.Parse("""{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}""")
        }, TestContext.Current.CancellationToken);

        return response!.Result!.Value.GetProperty("serverInfo").GetProperty("version").GetString();
    }

    private static async Task<string?> ModernServerVersionAsync(McpServerOptions options)
    {
        var response = await Server(options).ProcessRequestAsync(new JsonRpcRequest
        {
            Method = "server/discover",
            Id = JsonSerializer.SerializeToElement(1),
            Params = McpProtocol.BuildParamsWithMeta(null, McpProtocol.ModernVersion, "t", "0")
        }, TestContext.Current.CancellationToken);

        return response!.Result!.Value.GetProperty("_meta").GetProperty(McpProtocol.MetaServerInfo).GetProperty("version").GetString();
    }

    [Fact]
    public void ShouldHaveARelease_ToAnnounce()
    {
        Assert.False(string.IsNullOrWhiteSpace(Release));
        Assert.DoesNotContain('+', Release);
        // Read another way than the code under test reads it: the assembly version comes from the same VersionPrefix.
        Assert.StartsWith(typeof(McpServer).Assembly.GetName().Version!.ToString(3), Release, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAnnounceTheRelease_WhenNoServerVersionIsConfigured()
    {
        Assert.Equal("", new McpServerOptions().Version);
        Assert.Equal(Release, await LegacyServerVersionAsync(new McpServerOptions()));
        Assert.Equal(Release, await ModernServerVersionAsync(new McpServerOptions()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ShouldAnnounceTheRelease_WhenTheConfiguredServerVersionIsEmpty(string configured)
    {
        Assert.Equal(Release, await LegacyServerVersionAsync(new McpServerOptions { Version = configured }));
        Assert.Equal(Release, await ModernServerVersionAsync(new McpServerOptions { Version = configured }));
    }

    [Fact]
    public async Task ShouldAnnounceTheConfiguredVersion_WhenOneIsSet()
    {
        Assert.Equal("9.8.7-mine", await LegacyServerVersionAsync(new McpServerOptions { Version = "9.8.7-mine" }));
        Assert.Equal("9.8.7-mine", await ModernServerVersionAsync(new McpServerOptions { Version = "9.8.7-mine" }));
    }

    [Fact]
    public async Task ShouldAnnounceTheRelease_WhenTheClientInitializesALegacyServer()
    {
        await using var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(req => req.Method switch
        {
            "initialize" => new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonSerializer.SerializeToElement(new McpInitializeResult
                {
                    ProtocolVersion = "2025-06-18",
                    Capabilities = new McpServerCapabilities()
                })
            },
            _ => new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") }
        });
        await using var client = new McpClient(transport);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var initialize = transport.AllSentRequests.Single(r => r.Method == "initialize");
        var clientInfo = initialize.Params!.Value.GetProperty("clientInfo");
        Assert.Equal("Orkeon", clientInfo.GetProperty("name").GetString());
        Assert.Equal(Release, clientInfo.GetProperty("version").GetString());
    }

    [Fact]
    public async Task ShouldAnnounceTheRelease_WhenTheClientSpeaksToAModernServer()
    {
        await using var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(req => req.Method switch
        {
            "server/discover" => new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonSerializer.SerializeToElement(new McpDiscoverResult
                {
                    SupportedVersions = [McpProtocol.ModernVersion],
                    Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo() }
                })
            },
            "tools/list" => new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonSerializer.SerializeToElement(new McpToolListResult { ResultType = "complete" })
            },
            _ => new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") }
        });
        await using var client = new McpClient(transport);

        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await client.ListToolsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(McpProtocolEra.Modern, client.Era);
        var list = transport.AllSentRequests.Single(r => r.Method == "tools/list");
        var clientInfo = list.Params!.Value.GetProperty("_meta").GetProperty(McpProtocol.MetaClientInfo);
        Assert.Equal(Release, clientInfo.GetProperty("version").GetString());
    }
}
