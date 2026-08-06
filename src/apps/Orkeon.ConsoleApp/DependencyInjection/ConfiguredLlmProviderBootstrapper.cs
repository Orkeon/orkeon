using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using ILlmProvider = Orkeon.Domain.SharedKernel.ILlmProvider;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// Registers an <see cref="ILlmProvider"/> built from the <c>Llm</c> configuration section
/// (Model / BaseUrl / ApiKey / Temperature / MaxTokens / TimeoutSeconds).
/// </summary>
/// <remarks>
/// <para>
/// Without this, <c>AddOrkeonInfrastructure()</c> only registers <see cref="ILlmProvider"/> and
/// <see cref="IBasicLlmProvider"/> (via <c>TryAdd</c>) from <see cref="LlmConfig.Default()"/> — an
/// empty config with no API key and the default OpenAI endpoint. The crew runtime resolves that
/// provider, so <c>ctx.llm.act</c> ends up talking to the wrong endpoint with no credentials and
/// silently yields empty output: a request reports "done" while nothing real reaches the model.
/// </para>
/// <para>
/// MUST be called <em>before</em> <c>AddOrkeonInfrastructure()</c> so its <c>TryAdd</c> defaults
/// see these registrations as already present and skip them. The provider type is inferred from the
/// configured <c>BaseUrl</c> by <c>LlmProviderFactory</c> (e.g. <c>api.deepseek.com</c> → DeepSeek).
/// </para>
/// </remarks>
internal static class ConfiguredLlmProviderBootstrapper
{
    /// <summary>
    /// Reads the optional <c>Llm:Thinking</c> subsection into an <see cref="LlmThinkingConfig"/>.
    /// </summary>
    private static LlmThinkingConfig? ReadThinkingConfig(IConfigurationSection llmSection)
    {
        // Llm:Thinking:{Enabled,Effort} — forwarded to thinking-capable providers
        // (DeepSeek, Z.AI GLM) as the `thinking` block + `reasoning_effort` field.
        var thinkingSection = llmSection.GetSection("Thinking");
        if (!thinkingSection.Exists()) return null;
        return new LlmThinkingConfig
        {
            Enabled = bool.TryParse(thinkingSection["Enabled"], out var enabled) ? enabled : null,
            Effort = thinkingSection["Effort"],
        };
    }

    /// <summary>
    /// Binds the <c>Llm</c> section and registers a matching <see cref="ILlmProvider"/> +
    /// <see cref="IBasicLlmProvider"/>. No-op when the section is absent (the infrastructure
    /// default then stands).
    /// </summary>
    public static IServiceCollection AddConfiguredLlmProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Llm");
        if (!section.Exists())
            return services;

        var defaults = LlmConfig.Default();
#pragma warning disable CS0618 // ApiKey is the supported path for direct-from-config keys (appsettings Llm:ApiKey / Llm__ApiKey env).
        var config = defaults with
        {
            Model = section["Model"] ?? defaults.Model,
            BaseUrl = section["BaseUrl"] is { } baseUrl ? new Uri(baseUrl) : defaults.BaseUrl,
            ApiKey = section["ApiKey"] ?? defaults.ApiKey,
            Temperature = section.GetValue("Temperature", defaults.Temperature),
            MaxTokens = section.GetValue("MaxTokens", defaults.MaxTokens),
            TimeoutSeconds = section.GetValue("TimeoutSeconds", defaults.TimeoutSeconds),
            Thinking = ReadThinkingConfig(section),
        };
#pragma warning restore CS0618

        // Single source of truth: the underlying ILlmProvider. IBasicLlmProvider is its adapter,
        // so both surfaces share one configured instance.
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var factory = sp.GetRequiredService<ILlmProviderFactory>();
            var created = factory.Create(config);
            var provider = created switch
            {
                ILlmProvider direct => direct,
                LlmProviderAdapter adapter => adapter.UnderlyingProvider,
                _ => throw new InvalidOperationException(
                    $"LlmProviderFactory produced a {created?.GetType().FullName ?? "null"} that is neither " +
                    $"an ILlmProvider nor a LlmProviderAdapter; cannot expose it to the crew runtime."),
            };
            // Retry visibility: hand the host's observer (status line / ps) to the provider so
            // reconnection backoffs are shown instead of stalling the turn in silence.
            if (provider is Orkeon.Infrastructure.LLMs.Base.HttpLlmProviderBase httpProvider)
                httpProvider.RetryObserver = sp.GetService<ILlmRetryObserver>();
            return provider;
        });

        services.AddSingleton<IBasicLlmProvider>(sp =>
            new LlmProviderAdapter(sp.GetRequiredService<ILlmProvider>()));

        return services;
    }
}
