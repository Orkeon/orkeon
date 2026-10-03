using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Constants.Configuration;
using System.Diagnostics.CodeAnalysis;
namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// DI extension methods for registering MCP services: the client (<see cref="AddOrkeonMcp"/>) and
/// the server (<see cref="AddOrkeonMcpServer"/>).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class McpServiceExtensions
{
    /// <summary>
    /// Adds the MCP client: binds <see cref="McpOptions"/> from the
    /// <see cref="ConfigurationKeys.McpSection"/> section and registers <see cref="McpToolProvider"/>.
    /// It registers no server — <see cref="AddOrkeonMcpServer"/> does.
    /// </summary>
    /// <exception cref="InvalidOperationException">The section still carries the removed <c>EnableServer</c> key.</exception>
    public static IServiceCollection AddOrkeonMcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(ConfigurationKeys.McpSection);
        RefuseServerSwitch(section);
        services.Configure<McpOptions>(section);
        services.AddSingleton<McpToolProvider>();
        return services;
    }

    /// <summary>
    /// Adds the MCP server: <see cref="McpServer"/>, a singleton that serves the container's
    /// <c>IToolRegistry</c> and calls each tool through its <c>IToolInvocationPipeline</c> —
    /// <c>AddOrkeonInfrastructure()</c> and <c>AddOrkeonApplication()</c> register both —, with its
    /// <see cref="McpServerOptions"/> bound from <c>MCP:Server</c>. Registering is not serving: the
    /// host runs <see cref="McpServer.RunStdioAsync"/>, as <c>orkeon mcp serve</c> does, or
    /// <see cref="McpServer.ProcessRequestAsync"/> itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">The section still carries the removed <c>EnableServer</c> key.</exception>
    public static IServiceCollection AddOrkeonMcpServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(ConfigurationKeys.McpSection);
        RefuseServerSwitch(section);
        services.Configure<McpServerOptions>(section.GetSection("Server"));
        services.TryAddSingleton<McpServer>();
        return services;
    }

    /// <summary>
    /// <c>MCP:EnableServer</c> registered a server no shipped binary ever resolved (GAP-24): a host
    /// that still writes it, whatever the value, hears what replaced it instead of being ignored.
    /// </summary>
    private static void RefuseServerSwitch(IConfigurationSection section)
    {
        if (!section.GetSection("EnableServer").Exists())
            return;

        throw new InvalidOperationException(
            $"{ConfigurationKeys.McpSection}:EnableServer is not a setting any more: no setting starts an MCP server. " +
            "A C# host registers it with AddOrkeonMcpServer(configuration) and runs McpServer.RunStdioAsync(); " +
            "the orkeon tool serves its host's tools with `orkeon mcp serve`. Remove the key.");
    }
}
