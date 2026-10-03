using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// The one registration of a settings section (GAP-40): it binds the section, validates it when a host
/// starts and declares its shape, so a value the binder cannot convert, a rule of the options that
/// fails or a key the section does not carry refuses the start rather than the first run that reads
/// it.
/// </summary>
public static class SettingsServiceCollectionExtensions
{
    private static readonly IConfiguration s_noConfiguration = new ConfigurationBuilder().Build();

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to <paramref name="sectionPath"/> of the container's
    /// <see cref="IConfiguration"/>, validates it when a host starts
    /// (<see cref="SettingsDeclarationExtensions.DeclareSettings{TOptions}"/>) and declares the section.
    /// A container without configuration — a hand-built one — keeps the options' defaults. Registering a
    /// section twice for one options type registers it once.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="sectionPath">The section's configuration path, such as <c>Orkeon:Guardian</c>.</param>
    /// <returns>The options builder, to chain the section's <c>Validate</c> rules on.</returns>
    public static OptionsBuilder<TOptions> AddOrkeonSettings<TOptions>(this IServiceCollection services, string sectionPath)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);

        var builder = services.AddOptions<TOptions>();
        if (services.IsDeclared(sectionPath, typeof(TOptions)))
            return builder;

        builder.Configure<IServiceProvider>((options, provider) =>
            provider.GetService<IConfiguration>()?.GetSection(sectionPath).Bind(options));
        services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(provider =>
            new ConfigurationChangeTokenSource<TOptions>(builder.Name, provider.Configuration()));
        return builder.DeclareSettings(sectionPath);
    }

    /// <summary>The container's configuration, or an empty one in a container built without any.</summary>
    internal static IConfiguration Configuration(this IServiceProvider provider) =>
        provider.GetService<IConfiguration>() ?? s_noConfiguration;
}
