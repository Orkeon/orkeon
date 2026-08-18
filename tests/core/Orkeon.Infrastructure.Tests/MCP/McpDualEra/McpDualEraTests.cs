using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// Coverage of the dual-era MCP behavior (PUB-07): modern 2026-07-28 stateless
/// requests (per-request _meta, server/discover, UnsupportedProtocolVersionError)
/// next to the legacy initialize-handshake lineage, on both server and client.
/// </summary>
public class McpDualEraTests
{
    private static readonly string[] LegacyOnlyVersions = ["2025-11-25"];

    private static McpServer CreateServer(out MockToolRegistry registry)
    {
        registry = new MockToolRegistry();
        return new McpServer(registry, new McpServerOptions { Name = "TestServer", Version = "1.0.0" });
    }

    private static JsonRpcRequest ModernRequest(string method, string version, object? extraParams = null)
    {
        return new JsonRpcRequest
        {
            Method = method,
            Id = JsonSerializer.SerializeToElement(1),
            Params = McpProtocol.BuildParamsWithMeta(extraParams, version, "TestClient", "1.0")
        };
    }

    // ---- Server: discovery and version negotiation ----

    [Fact]
    public async Task Server_Discover_ReturnsSupportedVersionsAndIdentity()
    {
        var server = CreateServer(out _);

        var response = (await server.ProcessRequestAsync(
            ModernRequest("server/discover", McpProtocol.ModernVersion),
            TestContext.Current.CancellationToken))!;

        Assert.Null(response.Error);
        var result = JsonSerializer.Deserialize<McpDiscoverResult>(response.Result!.Value.GetRawText())!;
        Assert.Equal("complete", result.ResultType);
        Assert.Contains(McpProtocol.ModernVersion, result.SupportedVersions);
        Assert.Contains("2024-11-05", result.SupportedVersions);
        Assert.NotNull(result.Capabilities.Tools);
        Assert.True(result.Meta.HasValue);
        Assert.True(result.Meta!.Value.TryGetProperty(McpProtocol.MetaServerInfo, out var info));
        Assert.Equal("TestServer", info.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Server_ModernRequest_WithUnsupportedVersion_ReturnsError32022()
    {
        var server = CreateServer(out _);

        var response = (await server.ProcessRequestAsync(
            ModernRequest("tools/list", "1900-01-01"),
            TestContext.Current.CancellationToken))!;

        Assert.NotNull(response.Error);
        Assert.Equal(McpErrorCodes.UnsupportedProtocolVersion, response.Error!.Code);
        var supported = McpProtocol.ReadSupportedVersionsFromError(response.Error);
        Assert.Contains(McpProtocol.ModernVersion, supported);
        Assert.Equal("1900-01-01", response.Error.Data!.Value.GetProperty("requested").GetString());
    }

    [Fact]
    public async Task Server_LegacyInitialize_EchoesRequestedVersion_WhenSupported()
    {
        var server = CreateServer(out _);
        var request = new JsonRpcRequest
        {
            Method = "initialize",
            Id = JsonSerializer.SerializeToElement(1),
            Params = JsonSerializer.SerializeToElement(new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "old-client", version = "0.1" }
            })
        };

        var response = (await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken))!;

        Assert.Null(response.Error);
        var result = JsonSerializer.Deserialize<McpInitializeResult>(response.Result!.Value.GetRawText())!;
        Assert.Equal("2024-11-05", result.ProtocolVersion);
    }

    [Fact]
    public async Task Server_LegacyInitialize_AnswersNewestLegacy_WhenRequestedUnknown()
    {
        var server = CreateServer(out _);
        var request = new JsonRpcRequest
        {
            Method = "initialize",
            Id = JsonSerializer.SerializeToElement(1),
            Params = JsonSerializer.SerializeToElement(new { protocolVersion = "2019-01-01" })
        };

        var response = (await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken))!;

        var result = JsonSerializer.Deserialize<McpInitializeResult>(response.Result!.Value.GetRawText())!;
        Assert.Equal(McpProtocol.SupportedLegacyVersions[0], result.ProtocolVersion);
    }

    [Fact]
    public async Task Server_Notification_GetsNoResponse()
    {
        var server = CreateServer(out _);
        var notification = new JsonRpcRequest { Method = "notifications/initialized", Id = null };

        var response = await server.ProcessRequestAsync(notification, TestContext.Current.CancellationToken);

        Assert.Null(response);
    }

    [Fact]
    public async Task Server_ModernToolsList_CarriesCacheableResultFields()
    {
        var server = CreateServer(out var registry);
        registry.AddTool("zeta", new InlineMockBaseTool("zeta", "Z tool"));
        registry.AddTool("alpha", new InlineMockBaseTool("alpha", "A tool"));

        var response = (await server.ProcessRequestAsync(
            ModernRequest("tools/list", McpProtocol.ModernVersion),
            TestContext.Current.CancellationToken))!;

        var result = JsonSerializer.Deserialize<McpToolListResult>(response.Result!.Value.GetRawText())!;
        Assert.Equal("complete", result.ResultType);
        Assert.NotNull(result.TtlMs);
        Assert.Equal("private", result.CacheScope);
        // Deterministic ordinal ordering.
        string[] expectedOrder = ["alpha", "zeta"];
        Assert.Equal(expectedOrder, result.Tools.Select(t => t.Name).ToArray());
        Assert.True(result.Meta.HasValue);
    }

    [Fact]
    public async Task Server_Ping_ServedForLegacy_RejectedForModern()
    {
        var server = CreateServer(out _);

        var legacyPing = new JsonRpcRequest { Method = "ping", Id = JsonSerializer.SerializeToElement(7) };
        var legacyResponse = (await server.ProcessRequestAsync(legacyPing, TestContext.Current.CancellationToken))!;
        Assert.Null(legacyResponse.Error);

        var modernPing = ModernRequest("ping", McpProtocol.ModernVersion);
        var modernResponse = (await server.ProcessRequestAsync(modernPing, TestContext.Current.CancellationToken))!;
        Assert.NotNull(modernResponse.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, modernResponse.Error!.Code);
    }

    [Fact]
    public async Task Server_EchoesStringRequestIds()
    {
        var server = CreateServer(out _);
        var request = ModernRequest("server/discover", McpProtocol.ModernVersion);
        request.Id = JsonSerializer.SerializeToElement("discover-1");

        var response = (await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken))!;

        Assert.Equal("discover-1", response.Id!.Value.GetString());
    }

    // ---- Client: era detection ----

    private static JsonRpcResponse DiscoverResponse(JsonRpcRequest req, params string[] versions)
    {
        var result = new McpDiscoverResult
        {
            SupportedVersions = versions,
            Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo() },
            Meta = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                [McpProtocol.MetaServerInfo] = new { name = "modern-server", version = "2.0" }
            })
        };
        return new JsonRpcResponse
        {
            Id = req.Id,
            Result = JsonSerializer.SerializeToElement(result)
        };
    }

    [Fact]
    public async Task Client_DetectsModernServer_AndSendsPerRequestMeta()
    {
        await using var transport = new MockMcpTransport();
        transport.SetSendRequestFunc(req => req.Method switch
        {
            "server/discover" => DiscoverResponse(req, McpProtocol.ModernVersion),
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
        Assert.Equal(McpProtocol.ModernVersion, client.NegotiatedVersion);
        Assert.Equal("modern-server", client.ServerInfo?.Name);
        // Every modern request carries the protocol version in params._meta.
        var toolsRequest = transport.AllSentRequests.Single(r => r.Method == "tools/list");
        Assert.Equal(McpProtocol.ModernVersion, McpProtocol.TryReadRequestedVersion(toolsRequest.Params));
        // No legacy handshake happened.
        Assert.DoesNotContain(transport.AllSentRequests, r => r.Method == "initialize");
    }

    [Fact]
    public async Task Client_FallsBackToLegacy_WhenDiscoverIsRejected()
    {
        await using var transport = new MockMcpTransport();
        transport.SetSendRequestFunc(req => req.Method switch
        {
            "server/discover" => new JsonRpcResponse
            {
                Id = req.Id,
                Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "Method not found")
            },
            "initialize" => new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonSerializer.SerializeToElement(new McpInitializeResult
                {
                    ProtocolVersion = "2025-11-25",
                    Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo() },
                    ServerInfo = new McpServerInfo { Name = "legacy-server" }
                })
            },
            _ => new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") }
        });
        await using var client = new McpClient(transport);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(McpProtocolEra.Legacy, client.Era);
        Assert.Equal("2025-11-25", client.NegotiatedVersion);
        Assert.Equal("legacy-server", client.ServerInfo?.Name);
        // The legacy lineage requires notifications/initialized after the handshake.
        Assert.Contains(transport.AllSentNotifications, n => n.Method == "notifications/initialized");
    }

    [Fact]
    public async Task Client_Renegotiates_OnUnsupportedProtocolVersionError()
    {
        await using var transport = new MockMcpTransport();
        transport.SetSendRequestFunc(req =>
        {
            var version = McpProtocol.TryReadRequestedVersion(req.Params);
            if (req.Method == "server/discover" && version == McpProtocol.ModernVersion)
            {
                // Reject the preferred version, advertising 2025-11-25 only.
                var data = JsonSerializer.SerializeToElement(new { supported = LegacyOnlyVersions, requested = version });
                return new JsonRpcResponse
                {
                    Id = req.Id,
                    Error = new JsonRpcError(McpErrorCodes.UnsupportedProtocolVersion, "Unsupported protocol version") { Data = data }
                };
            }
            if (req.Method == "server/discover")
            {
                return DiscoverResponse(req, "2025-11-25");
            }
            return new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") };
        });
        await using var client = new McpClient(transport);

        // The mock's error advertises our full supported list; the retry must land
        // on the newest mutually supported revision below the rejected one.
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(McpProtocolEra.Modern, client.Era);
        Assert.Equal("2025-11-25", client.NegotiatedVersion);
    }

    [Fact]
    public async Task Client_SurfacesMrtrInterimResults_AsErrors()
    {
        await using var transport = new MockMcpTransport();
        transport.SetSendRequestFunc(req => req.Method switch
        {
            "server/discover" => DiscoverResponse(req, McpProtocol.ModernVersion),
            "tools/call" => new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonSerializer.SerializeToElement(new { resultType = "input_required", inputRequests = Array.Empty<object>() })
            },
            _ => new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") }
        });
        await using var client = new McpClient(transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("anything", ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("MRTR", result.Content[0].Text);
    }

    [Fact]
    public async Task Client_ThrowsActionableError_WhenNoCommonVersion()
    {
        await using var transport = new MockMcpTransport();
        transport.SetSendRequestFunc(req => DiscoverResponse(req, "2099-01-01"));
        await using var client = new McpClient(transport);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Contains("2099-01-01", ex.Message);
        Assert.Contains(McpProtocol.ModernVersion, ex.Message);
    }
}
