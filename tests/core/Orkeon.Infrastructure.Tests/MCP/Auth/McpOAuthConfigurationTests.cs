using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.MCP.Auth;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.Tests.MCP.Auth;

/// <summary>FWK-16: the <c>Auth</c> keys of an OAuth server, their validation, and the client that reaches the token endpoint.</summary>
public sealed class McpOAuthConfigurationTests
{
    private const string Variable = "TRACKER_OAUTH_CLIENT_KEY";

    private static string? Env(string name) => name == Variable ? "canary-7f3a-do-not-leak" : null;

    private static McpServerConfig Server(McpAuthType type)
    {
        var config = new McpServerConfig { Url = new Uri("https://mcp.example.test/"), Transport = McpTransportType.Sse };
        config.Auth.Type = type;
        config.Auth.TokenUrl = new Uri("https://idp.example.test/oauth/token");
        config.Auth.ClientId = "orkeon-agent";
        config.Auth.ClientSecretEnv = Variable;
        return config;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test double; nothing it holds needs releasing.")]
    private static McpOAuthServices Services(IOAuthLoginInteraction? interaction = null)
    {
        var factory = new MockHttpClientFactory();
        factory.SetupDefaultHandler();
        return new McpOAuthServices(factory, interaction, TimeProvider.System);
    }

    [Theory]
    [InlineData("o-auth2-device")]
    [InlineData("OAUTH2DEVICE")]
    [InlineData("oauth2device")]
    [InlineData("oauth2_device")]
    [InlineData("OAuth2-Device")]
    [InlineData("BEARER")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData("--")]
    public void ShouldRefuseAnAuthTypeSpellingThatIsNotListed_NamingTheOnesThatAre(string text)
    {
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(McpAuthType));

        var ex = Assert.Throws<FormatException>(() => converter.ConvertFromInvariantString(text));

        Assert.Contains($"'{text}' is not an MCP Auth:Type", ex.Message, StringComparison.Ordinal);
        Assert.Contains("oauth2-device", ex.Message, StringComparison.Ordinal);
        Assert.Contains("oauth2-client-credentials", ex.Message, StringComparison.Ordinal);
        Assert.Contains("OAuth2Device", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Bearer", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("None", McpAuthType.None)]
    [InlineData("none", McpAuthType.None)]
    [InlineData("Basic", McpAuthType.Basic)]
    [InlineData("basic", McpAuthType.Basic)]
    [InlineData("OAuth2Device", McpAuthType.OAuth2Device)]
    [InlineData("OAuth2ClientCredentials", McpAuthType.OAuth2ClientCredentials)]
    [InlineData("oauth2-client-credentials", McpAuthType.OAuth2ClientCredentials)]
    [InlineData("oauth2-device", McpAuthType.OAuth2Device)]
    [InlineData("bearer", McpAuthType.Bearer)]
    [InlineData("Bearer", McpAuthType.Bearer)]
    public void ShouldAcceptTheListedAuthTypeSpellings(string text, McpAuthType expected)
    {
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(McpAuthType));

        Assert.Equal(expected, converter.ConvertFromInvariantString(text));
    }

    [Theory]
    [InlineData("oauth2-client-credentials", McpAuthType.OAuth2ClientCredentials)]
    [InlineData("oauth2-device", McpAuthType.OAuth2Device)]
    public void ShouldBindTheHyphenatedAuthType_FromTheSettings(string text, McpAuthType expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MCP:Servers:tracker:Url"] = "https://mcp.example.test/",
            ["MCP:Servers:tracker:Transport"] = "Sse",
            ["MCP:Servers:tracker:Auth:Type"] = text,
            ["MCP:Servers:tracker:Auth:TokenUrl"] = "https://idp.example.test/oauth/token",
            ["MCP:Servers:tracker:Auth:ClientId"] = "orkeon-agent",
            ["MCP:Servers:tracker:Auth:Scopes:0"] = "tools.call",
            ["MCP:Servers:tracker:Auth:Scopes:1"] = "tools.read",
            ["MCP:Servers:tracker:Auth:Audience"] = "https://mcp.example.test",
        }).Build();

        var options = configuration.GetSection("MCP").Get<McpOptions>()!;

        var auth = options.Servers["tracker"].Auth;
        Assert.Equal(expected, auth.Type);
        Assert.Equal("orkeon-agent", auth.ClientId);
        Assert.Equal(["tools.call", "tools.read"], auth.Scopes);
        Assert.Equal("https://mcp.example.test", auth.Audience);
    }

    [Fact]
    public void ShouldBuildAnAuthenticator_ForAValidClientCredentialsServer()
    {
        var authenticator = McpRequestAuthenticators.Create("tracker", Server(McpAuthType.OAuth2ClientCredentials), Env, Services());

        Assert.IsType<OAuthMcpAuthenticator>(authenticator);
    }

    [Theory]
    [InlineData("TokenUrl")]
    [InlineData("ClientId")]
    [InlineData("ClientSecretEnv")]
    public void ShouldNameTheKey_WhenAClientCredentialsKeyIsMissing(string key)
    {
        var config = Server(McpAuthType.OAuth2ClientCredentials);
        switch (key)
        {
            case "TokenUrl": config.Auth.TokenUrl = null; break;
            case "ClientId": config.Auth.ClientId = null; break;
            default: config.Auth.ClientSecretEnv = null; break;
        }

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("tracker", config, Env, Services()));

        Assert.Equal($"MCP:Servers:tracker:Auth:{key}", ex.Key);
    }

    [Fact]
    public void ShouldNameTheVariable_WhenTheClientSecretVariableIsNotSet()
    {
        var config = Server(McpAuthType.OAuth2ClientCredentials);
        config.Auth.ClientSecretEnv = "ORKEON_TEST_ABSENT_CLIENT_KEY";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("tracker", config, Env, Services()));

        Assert.Equal("MCP:Servers:tracker:Auth:ClientSecretEnv", ex.Key);
        Assert.Contains("ORKEON_TEST_ABSENT_CLIENT_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://idp.example.test/oauth/token")]
    [InlineData("not a url")]
    [InlineData("/relative/token")]
    public void ShouldRefuseATokenUrl_ThatIsNotHttpsOrLoopback(string url)
    {
        var config = Server(McpAuthType.OAuth2ClientCredentials);
        config.Auth.TokenUrl = new Uri(url, UriKind.RelativeOrAbsolute);

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("tracker", config, Env, Services()));

        Assert.Equal("MCP:Servers:tracker:Auth:TokenUrl", ex.Key);
    }

    [Fact]
    public void ShouldAcceptAPlainHttpTokenUrl_OnLoopback()
    {
        var config = Server(McpAuthType.OAuth2ClientCredentials);
        config.Auth.TokenUrl = new Uri("http://127.0.0.1:9000/token");

        Assert.NotNull(McpRequestAuthenticators.Create("tracker", config, Env, Services()));
    }

    [Fact]
    public void ShouldRefuseTheDeviceFlow_NamingTheServer_WhenNoInteractionIsRegistered()
    {
        var config = Server(McpAuthType.OAuth2Device);
        config.Auth.DeviceAuthorizationUrl = new Uri("https://idp.example.test/oauth/device");

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("tracker", config, Env, Services(interaction: null)));

        Assert.Equal("MCP:Servers:tracker:Auth:Type", ex.Key);
        Assert.Contains("tracker", ex.Message, StringComparison.Ordinal);
        Assert.Contains("oauth2-device", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldAcceptTheDeviceFlow_WhenAnInteractionIsRegistered()
    {
        var config = Server(McpAuthType.OAuth2Device);
        config.Auth.DeviceAuthorizationUrl = new Uri("https://idp.example.test/oauth/device");
        config.Auth.ClientSecretEnv = null;

        var authenticator = McpRequestAuthenticators.Create("tracker", config, Env, Services(new FakeOAuthLoginInteraction()));

        Assert.IsType<OAuthMcpAuthenticator>(authenticator);
    }

    [Fact]
    public void ShouldNameTheKey_WhenTheDeviceAuthorizationUrlIsMissing()
    {
        var config = Server(McpAuthType.OAuth2Device);

        var ex = Assert.Throws<McpServerConfigurationException>(() =>
            McpRequestAuthenticators.Create("tracker", config, Env, Services(new FakeOAuthLoginInteraction())));

        Assert.Equal("MCP:Servers:tracker:Auth:DeviceAuthorizationUrl", ex.Key);
    }

    [Fact]
    public void ShouldRefuseOAuth_OnAStdioServer()
    {
        var config = Server(McpAuthType.OAuth2ClientCredentials);
        config.Transport = McpTransportType.Stdio;
        config.Command = "server";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("tracker", config, Env, Services()));

        Assert.Equal("MCP:Servers:tracker:Auth", ex.Key);
    }

    [Fact]
    public void ShouldNameTheTokenClient_AndKeepItOutOfTheLlmExchangeLog()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/logs"));
        services.AddSingleton<ILlmExchangeLogger>(new NullExchangeLogger());
        services.AddLlmExchangeLogging("/logs");
        services.AddOrkeonMcp(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Equal("orkeon-mcp-oauth", McpServiceExtensions.OAuthHttpClientName);
        var excluded = provider.GetServices<LlmExchangeLogExclusion>().Select(e => e.ClientName);
        Assert.Contains(McpServiceExtensions.OAuthHttpClientName, excluded);
    }

    private sealed class NullExchangeLogger : ILlmExchangeLogger
    {
        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
