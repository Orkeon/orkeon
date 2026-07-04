using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;

namespace Orkeon.Tools.EventHub.DependencyInjection;

/// <summary>
/// Auto-registration of the seven EventHub agent tools.
/// Mirrors the sibling pattern used by Orkeon.Tools.Data / Orkeon.Tools.Code / Orkeon.Tools.Web /
/// Orkeon.Tools.FileSystem (see <c>AddOrkeon*Tools</c>).
/// </summary>
public static class EventHubToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the seven EventHub tools as <see cref="IBaseTool"/> transients. Requires
    /// <c>IEventHub</c> and <c>IEventHubCallerContext</c> to be registered (see
    /// <c>EventHubServiceCollectionExtensions.AddOrkeonInMemoryEventHub</c>).
    /// </summary>
    public static IServiceCollection AddOrkeonEventHubTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<IBaseTool, PublishEventTool>();
        services.AddTransient<IBaseTool, PostMessageTool>();
        services.AddTransient<IBaseTool, SendRequestTool>();
        services.AddTransient<IBaseTool, ReplyToTool>();
        services.AddTransient<IBaseTool, ReceiveMessageTool>();
        services.AddTransient<IBaseTool, WaitForEventTool>();
        services.AddTransient<IBaseTool, GetLastValueTool>();
        return services;
    }
}
