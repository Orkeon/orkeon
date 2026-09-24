using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;

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
    /// <see cref="ILlmUsageSink"/> (<see cref="MeteredLlmProvider"/>). A provider registered any
    /// other way would spend tokens no meter sees (STUDIO-42).
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
            MeteredLlmProvider.Wrap(provider(sp), sp.GetService<ILlmUsageSink>()));
        services.AddSingleton<IBasicLlmProvider>(sp =>
            new LlmProviderAdapter(sp.GetRequiredService<ILlmProvider>()));
        services.AddSingleton<IChatClient>(sp =>
            new LlmProviderToChatClientAdapter(
                sp.GetRequiredService<ILlmProvider>(),
                baseConfig,
                textFallbackParser: sp.GetService<IToolCallParser>()));
        return services;
    }
}
