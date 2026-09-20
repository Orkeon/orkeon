using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.Configuration;
using System.Diagnostics.CodeAnalysis;
namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// DI extension methods for registering MCP services.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class McpServiceExtensions
{
    /// <summary>
    /// Adds MCP client and (optionally) server services to the DI container.
    /// Reads configuration from the <see cref="ConfigurationKeys.McpSection"/> section.
    /// </summary>
    public static IServiceCollection AddOrkeonMcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(ConfigurationKeys.McpSection);
        services.Configure<McpOptions>(section);
        services.AddSingleton<McpToolProvider>();

        var mcpOptions = section.Get<McpOptions>() ?? new McpOptions();
        if (mcpOptions.EnableServer)
        {
            services.Configure<McpServerOptions>(section.GetSection("Server"));
            services.AddSingleton<McpServer>();
        }

        return services;
    }
}
