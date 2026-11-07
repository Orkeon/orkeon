using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// GAP-24: the MCP server is registered by <c>AddOrkeonMcpServer</c>, never by a setting.
/// </summary>
public sealed class McpServiceExtensionsTests
{
    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddOrkeonMcp_registers_the_client_and_never_the_server()
    {
        var services = new ServiceCollection();

        services.AddOrkeonMcp(Settings(new() { ["MCP:Servers:demo:Command"] = "demo-mcp" }));

        Assert.Contains(services, d => d.ServiceType == typeof(McpToolProvider));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(McpServer));
    }

    [Fact]
    public async Task AddOrkeonMcpServer_registers_a_server_that_introduces_itself_with_MCP_Server()
    {
        var configuration = Settings(new()
        {
            ["MCP:Server:Name"] = "Acme tools",
            ["MCP:Server:Version"] = "2.1.0",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonLlmProvider(_ => new MockLlmProvider());
        services.AddOrkeonInfrastructure();
        services.AddOrkeonApplication();
        services.AddOrkeonMcpServer(configuration);
        await using var host = services.BuildServiceProvider();

        var response = await host.GetRequiredService<McpServer>().ProcessRequestAsync(
            new JsonRpcRequest
            {
                Method = "initialize",
                Id = JsonSerializer.SerializeToElement(1),
                Params = JsonElement.Parse("""{ "protocolVersion": "2025-11-25", "capabilities": {} }"""),
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        var result = JsonSerializer.Deserialize<McpInitializeResult>(response.Result!.Value.GetRawText());
        Assert.NotNull(result?.ServerInfo);
        Assert.Equal("Acme tools", result.ServerInfo.Name);
        Assert.Equal("2.1.0", result.ServerInfo.Version);
    }
}
