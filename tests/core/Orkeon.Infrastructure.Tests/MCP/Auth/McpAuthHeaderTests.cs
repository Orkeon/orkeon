using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.MCP.Auth;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP.Auth;

/// <summary>FWK-03: the request authenticator puts the configured credentials on every MCP request.</summary>
public sealed class McpAuthHeaderTests
{
    private static readonly Uri Endpoint = new("https://mcp.example.test/rpc");

    private static string? Env(string name) => name switch
    {
        "TEST_MCP_TOKEN" => "s3cret",
        "TEST_MCP_USER" => "ann",
        "TEST_MCP_PASSWORD" => "pw",
        "TEST_MCP_TENANT" => "acme",
        _ => null,
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The handler is owned by the HttpClient, which is disposed here.")]
    private static async Task<HttpRequestMessage> SendOneAsync(McpServerConfig config)
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":1,"result":{}}""", "application/json");
        using var http = new HttpClient(handler);
        var authenticator = McpRequestAuthenticators.Create("srv", config, Env);
        await using var transport = new SseMcpTransport(Endpoint, http, authenticator: authenticator);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        var request = new JsonRpcRequest { Id = JsonSerializer.SerializeToElement(1), Method = "tools/list" };
        await transport.SendRequestAsync(request, TestContext.Current.CancellationToken);
        return handler.LastRequest!;
    }

    private static McpServerConfig Sse() => new() { Url = Endpoint, Transport = McpTransportType.Sse };

    [Fact]
    public async Task ShouldSendBearerFromEnvironment_WhenAuthTypeIsBearer()
    {
        var config = Sse();
        config.Auth.Type = McpAuthType.Bearer;
        config.Auth.TokenEnv = "TEST_MCP_TOKEN";

        var sent = await SendOneAsync(config);

        Assert.Equal("Bearer s3cret", sent.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ShouldSendBasicFromEnvironment_WhenAuthTypeIsBasic()
    {
        var config = Sse();
        config.Auth.Type = McpAuthType.Basic;
        config.Auth.UserEnv = "TEST_MCP_USER";
        config.Auth.PasswordEnv = "TEST_MCP_PASSWORD";

        var sent = await SendOneAsync(config);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("ann:pw"));
        Assert.Equal($"Basic {expected}", sent.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ShouldSendStaticAndEnvHeaders_WhenHeadersAreConfigured()
    {
        var config = Sse();
        config.Headers["X-Tenant"] = "env:TEST_MCP_TENANT";
        config.Headers["X-Plain"] = "literal";

        var sent = await SendOneAsync(config);

        Assert.Equal("acme", sent.Headers.GetValues("X-Tenant").Single());
        Assert.Equal("literal", sent.Headers.GetValues("X-Plain").Single());
        Assert.Null(sent.Headers.Authorization);
    }

    [Fact]
    public void ShouldReturnNoAuthenticator_WhenNothingIsConfigured()
    {
        Assert.Null(McpRequestAuthenticators.Create("srv", Sse(), Env));
    }

    [Fact]
    public async Task ShouldRefuseTheRequestNamingTheKey_WhenTheTokenVariableIsEmptyAtRequestTime()
    {
        var token = "s3cret";
        var config = Sse();
        config.Auth.Type = McpAuthType.Bearer;
        config.Auth.TokenEnv = "TEST_MCP_TOKEN";
        var authenticator = McpRequestAuthenticators.Create("srv", config, _ => token)!;
        token = "";
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);

        var ex = await Assert.ThrowsAsync<McpServerConfigurationException>(
            async () => await authenticator.ApplyAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("MCP:Servers:srv:Auth:TokenEnv", ex.Key);
        Assert.Null(request.Headers.Authorization);
    }
}
