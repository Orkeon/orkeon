using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Registration of a language model the provider factory does not build: the echo provider
/// of a host without an <c>Llm</c> section, a Microsoft Agent Framework agent used as a model,
/// a test double.
/// </summary>
public static class LlmProviderRegistrationExtensions
{
    /// <summary>
    /// Registers <paramref name="provider"/> as the host's language model on the three surfaces
    /// the runtime consumes — <see cref="ILlmProvider"/>, <see cref="IBasicLlmProvider"/> and
    /// <see cref="IChatClient"/> — all three over one instance, metered for the host's
    /// <see cref="ILlmUsageSink"/> (<see cref="MeteredLlmProvider"/>) and limited by its
    /// <see cref="ILlmRateLimiter"/> (<see cref="RateLimitedLlmProvider"/>, GAP-38). A provider
    /// registered any other way would spend tokens no meter sees (STUDIO-42), past every cap.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="provider">Builds the provider, once, from the container.</param>
    /// <param name="baseConfig">
    /// The configuration the chat client merges per-call options into (model, key, base URL);
    /// null lets the provider keep its own.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddOrkeonLlmProvider(
        this IServiceCollection services,
        Func<IServiceProvider, ILlmProvider> provider,
        LlmConfig? baseConfig = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);

        services.AddSingleton<ILlmProvider>(sp =>
            LlmProviderEntrance.Enter(provider(sp), sp.GetService<ILlmUsageSink>(), sp.GetService<ILlmRateLimiter>()));
        services.AddSingleton<IBasicLlmProvider>(sp =>
            new LlmProviderAdapter(sp.GetRequiredService<ILlmProvider>()));
        services.AddSingleton<IChatClient>(sp =>
            new LlmProviderToChatClientAdapter(
                sp.GetRequiredService<ILlmProvider>(),
                baseConfig,
                textFallbackParser: sp.GetService<IToolCallParser>()));
        return services;
    }

    /// <summary>
    /// Registers a named LLM profile over a provider the factory does not build — a test
    /// double, a Microsoft Agent Framework agent (GAP-17). A crew runs an agent on it with
    /// <c>llm: { profile: &lt;name&gt; }</c>; its provider is built once, on first use, and
    /// metered and limited like the default one.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name crews reference; <c>default</c> is reserved.</param>
    /// <param name="provider">Builds the provider, once, from the container.</param>
    /// <param name="baseConfig">
    /// The configuration the profile's chat client merges per-call options into (its model,
    /// key, base URL); null takes the provider's own.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddOrkeonLlmProfile(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, ILlmProvider> provider,
        LlmConfig? baseConfig = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(provider);

        // Metered and limited here, the second entrance of the metered path, like AddOrkeonLlmProvider.
        services.AddSingleton(new LlmProfileRegistration(
            name.Trim(),
            sp => LlmProviderEntrance.Enter(provider(sp), sp.GetService<ILlmUsageSink>(), sp.GetService<ILlmRateLimiter>()),
            baseConfig));
        services.TryAddSingleton<ILlmProfileRegistry, LlmProfileRegistry>();
        return services;
    }

    /// <summary>
    /// Registers every profile the host configures under <c>Llm:Profiles:&lt;name&gt;</c>, each
    /// of the <c>Llm</c> section's shape and built by the provider factory (GAP-17). The
    /// profiles are read and validated now — a reserved name, an invalid <c>BaseUrl</c> or an
    /// unreadable number fails the host build with the key to fix.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A profile is invalid.</exception>
    public static IServiceCollection AddOrkeonLlmProfiles(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var (name, config) in LlmSettings.ReadProfiles(configuration))
        {
            services.AddOrkeonLlmProfile(name, sp =>
                sp.GetRequiredService<ILlmProviderFactory>().Create(config) is LlmProviderAdapter adapter
                    ? adapter.UnderlyingProvider
                    : throw new InvalidOperationException(
                        $"The LLM provider of profile '{name}' does not expose ILlmProvider."),
                config);
        }

        services.TryAddSingleton<ILlmProfileRegistry, LlmProfileRegistry>();
        return services;
    }
}
