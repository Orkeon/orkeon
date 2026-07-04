using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Tools;

namespace Orkeon.Tools.Abstractions.DependencyInjection;

/// <summary>
/// Extension methods for registering tools declared in Orkeon.Tools.Abstractions.
/// </summary>
public static class AbstractionToolExtensions
{
    /// <summary>
    /// Adds the abstraction-level tools (currently: list_mounts) to the service collection.
    /// Mirrors the sibling pattern used by FileSystem/Code/Data/Web/Analysis tool packages
    /// so crews can advertise list_mounts in their tool list without it being silently
    /// skipped by the registry. See experiment 07 issue #13.
    /// </summary>
    public static IServiceCollection AddOrkeonAbstractionTools(this IServiceCollection services)
    {
        services.AddTransient<IBaseTool, ListMountsTool>();
        return services;
    }
}
