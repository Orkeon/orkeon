using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration helpers for Orkeon.Cli core services.
/// </summary>
public static class CliServiceCollectionExtensions
{
    /// <summary>
    /// Registers the three default commands (Help, Exit, Clear) and the
    /// <see cref="DefaultCommandRegistry"/> as singletons. Call once at
    /// application startup.
    /// </summary>
    /// <remarks>
    /// Specific registries (e.g. <c>QaCommandRegistry</c>, <c>MainMenuCommandRegistry</c>)
    /// and concrete runners (e.g. <c>QaRunner</c>, <c>MainMenuRunner</c>) must be
    /// registered separately by the host application. See spec §7.1.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddOrkeonCli();
    /// services.AddSingleton&lt;MyCommand&gt;();
    /// services.AddSingleton&lt;MyCommandRegistry&gt;();
    /// services.AddSingleton&lt;MyRunner&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddOrkeonCli(this IServiceCollection services)
    {
        services.AddSingleton<HelpCommand>();
        services.AddSingleton<ExitCommand>();
        services.AddSingleton<ClearCommand>();
        services.AddSingleton<DefaultCommandRegistry>();
        return services;
    }
}
