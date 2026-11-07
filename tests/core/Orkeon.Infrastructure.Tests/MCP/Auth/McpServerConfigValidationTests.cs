using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.MCP.Auth;

namespace Orkeon.Infrastructure.Tests.MCP.Auth;

/// <summary>FWK-03: an invalid server configuration is refused with an error naming the key.</summary>
public sealed class McpServerConfigValidationTests
{
    private static string? NoEnv(string _) => null;

    private static McpServerConfig Sse() => new() { Url = new Uri("https://mcp.example.test/"), Transport = McpTransportType.Sse };

    [Fact]
    public void ShouldNameTheKey_WhenAuthorizationIsALiteralHeader()
    {
        var config = Sse();
        config.Headers["Authorization"] = "Bearer abc";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:Headers:Authorization", ex.Key);
        Assert.Contains("MCP:Servers:srv:Headers:Authorization", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNameTheKey_WhenTheBearerVariableIsAbsent()
    {
        var config = Sse();
        config.Auth.Type = McpAuthType.Bearer;
        config.Auth.TokenEnv = "ORKEON_TEST_ABSENT_TOKEN";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:Auth:TokenEnv", ex.Key);
        Assert.Contains("ORKEON_TEST_ABSENT_TOKEN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNameTheKey_WhenBearerHasNoTokenEnv()
    {
        var config = Sse();
        config.Auth.Type = McpAuthType.Bearer;

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:Auth:TokenEnv", ex.Key);
    }

    [Fact]
    public void ShouldNameTheKey_WhenAHeaderVariableIsAbsent()
    {
        var config = Sse();
        config.Headers["X-Key"] = "env:ORKEON_TEST_ABSENT_HEADER";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:Headers:X-Key", ex.Key);
    }

    [Fact]
    public void ShouldNameTheKey_WhenCallTimeoutIsNotPositive()
    {
        var config = Sse();
        config.CallTimeout = TimeSpan.Zero;

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:CallTimeout", ex.Key);
    }

    [Fact]
    public void ShouldNameTheKey_WhenConnectTimeoutIsNotPositive()
    {
        var config = Sse();
        config.ConnectTimeout = TimeSpan.FromSeconds(-1);

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:ConnectTimeout", ex.Key);
    }

    [Fact]
    public void ShouldRefuseAuth_WhenTheTransportIsStdio()
    {
        var config = new McpServerConfig { Command = "demo-mcp", Transport = McpTransportType.Stdio };
        config.Auth.Type = McpAuthType.Bearer;
        config.Auth.TokenEnv = "TEST_PRESENT";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, _ => "x"));

        Assert.Equal("MCP:Servers:srv:Auth", ex.Key);
    }

    [Fact]
    public void ShouldRefuseHeaders_WhenTheTransportIsStdio()
    {
        var config = new McpServerConfig { Command = "demo-mcp", Transport = McpTransportType.Stdio };
        config.Headers["X-Plain"] = "literal";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, NoEnv));

        Assert.Equal("MCP:Servers:srv:Headers", ex.Key);
    }
}
