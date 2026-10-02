using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Scripting.Runtime;
using ILlmProvider = Orkeon.Domain.SharedKernel.ILlmProvider;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// Registers the REPL's language model the way <c>orkeon run</c> does: the provider the
/// <c>Llm</c> section describes, on the three surfaces the runtime consumes —
/// <see cref="ILlmProvider"/>, <see cref="IBasicLlmProvider"/> and
/// <see cref="Microsoft.Extensions.AI.IChatClient"/>, the chat client on that section's
/// configuration — and the named profiles under <c>Llm:Profiles</c>. Without an <c>Llm</c>
/// section, the echo provider, announced with the runner's warning.
/// </summary>
/// <remarks>
/// <para>
/// The REPL registered its provider as <see cref="ILlmProvider"/> and
/// <see cref="IBasicLlmProvider"/> only: its chat client was the infrastructure's fallback, a
/// second provider built from an empty configuration — OpenAI's endpoint, no key — and every
/// agent turn, RAG answer or judge it served answered "API key is required" (GAP-29). The
/// infrastructure registers no model of its own any more.
/// </para>
/// <para>
/// The section is read by <see cref="LlmSettings"/>, the reader the runners use: the REPL accepts
/// exactly the keys <c>orkeon run</c> does. The provider type is inferred from the configured
/// <c>BaseUrl</c> by <c>LlmProviderFactory</c> (e.g. <c>api.deepseek.com</c> → DeepSeek).
/// </para>
/// </remarks>
internal static partial class ConfiguredLlmProviderBootstrapper
{
    /// <summary>
    /// Registers the REPL's model from <paramref name="configuration"/>: the <c>Llm</c> section's
    /// provider, else the echo provider; and every <c>Llm:Profiles</c> entry.
    /// </summary>
    public static IServiceCollection AddConfiguredLlmProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Llm:Profiles:<name> — the named providers a crew may pick per agent (GAP-17).
        services.AddOrkeonLlmProfiles(configuration);

        if (!LlmSettings.HasDefault(configuration))
        {
            // No default section (none at all, or one holding profiles alone): the echo
            // provider, as orkeon run falls back to, and the same warning, once — when the
            // provider is first resolved.
            services.AddOrkeonLlmProvider(sp => Announced(sp, new UndefinedLlmProvider()));
            return services;
        }

        // A section without Model names none: the provider runs its own default (GAP-18).
        var config = LlmSettings.ReadDefault(configuration);

        // One instance on the three surfaces: the provider the factory builds — metered there —
        // and the chat client on the section's configuration, as orkeon run serves it.
        services.AddOrkeonLlmProvider(sp =>
        {
            var created = sp.GetRequiredService<ILlmProviderFactory>().Create(config);
            var provider = created switch
            {
                ILlmProvider direct => direct,
                LlmProviderAdapter adapter => adapter.UnderlyingProvider,
                _ => throw new InvalidOperationException(
                    $"LlmProviderFactory produced a {created?.GetType().FullName ?? "null"} that is neither " +
                    $"an ILlmProvider nor a LlmProviderAdapter; cannot expose it to the crew runtime."),
            };
            // Retry visibility: hand the host's observer (status line / ps) to the provider so
            // reconnection backoffs are shown instead of stalling the turn in silence. The
            // vendor sits under the token meter; the metered provider is what gets exposed.
            if (MeteredLlmProvider.Unwrap(provider) is Orkeon.Infrastructure.LLMs.Base.HttpLlmProviderBase httpProvider)
                httpProvider.RetryObserver = sp.GetService<ILlmRetryObserver>();
            return provider;
        }, config);

        return services;
    }

    /// <summary>The echo provider, once the warning that says the REPL runs on it is logged.</summary>
    private static UndefinedLlmProvider Announced(IServiceProvider services, UndefinedLlmProvider echo)
    {
        if (services.GetService<ILoggerFactory>() is { } loggers)
            LogLlmNotConfigured(loggers.CreateLogger("Orkeon.ConsoleApp"));
        return echo;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = OperatorMessages.LlmNotConfigured)]
    private static partial void LogLlmNotConfigured(ILogger logger);
}
