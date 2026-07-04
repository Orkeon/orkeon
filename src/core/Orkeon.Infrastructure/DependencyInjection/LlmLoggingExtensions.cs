using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Logging;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering LLM exchange logging in the DI container.
/// </summary>
public static class LlmLoggingExtensions
{
    /// <summary>
    /// Registers the LLM exchange logging system with both JSON file logging and structured logging.
    /// <para>
    /// This registers:
    /// <list type="bullet">
    ///   <item><see cref="LlmLoggingDelegatingHandler"/> — HTTP interceptor for all HTTP clients</item>
    ///   <item><see cref="LlmExchangeJsonLogger"/> — full JSON Lines file capture</item>
    ///   <item><see cref="LlmExchangeStructuredLogger"/> — ILogger-based summary logging</item>
    ///   <item><see cref="CompositeLlmExchangeLogger"/> — dispatches to both loggers</item>
    /// </list>
    /// </para>
    /// <para>
    /// The handler is injected into <b>all</b> <see cref="IHttpClientFactory"/> clients via
    /// <c>ConfigureHttpClientDefaults</c>, so every LLM provider is automatically intercepted
    /// without per-provider registration.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="logDirectory">Directory for .jsonl exchange logs.</param>
    /// <param name="options">Optional logging options. When <see langword="null"/>, defaults are used.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLlmExchangeLogging(
        this IServiceCollection services,
        string logDirectory,
        LlmLoggingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        options ??= LlmLoggingOptions.Default;

        // Make options resolvable from DI so handlers built outside the
        // ConfigureHttpClientDefaults pipeline (e.g. RaggableTree's embedding
        // HttpClient) pick up the same configuration.
        services.TryAddSingleton(options);

        // Register JSON file logger as singleton (manages file locks)
        services.TryAddSingleton(sp =>
            new LlmExchangeJsonLogger(
                sp.GetRequiredService<IFileSystemService>(),
                logDirectory,
                sp.GetRequiredService<ILogger<LlmExchangeJsonLogger>>()));

        // Register structured logger as singleton
        services.TryAddSingleton(sp =>
            new LlmExchangeStructuredLogger(
                sp.GetRequiredService<ILogger<LlmExchangeStructuredLogger>>()));

        // Register composite logger as the primary ILlmExchangeLogger
        services.TryAddSingleton<ILlmExchangeLogger>(sp =>
            new CompositeLlmExchangeLogger(
                sp.GetRequiredService<LlmExchangeJsonLogger>(),
                sp.GetRequiredService<LlmExchangeStructuredLogger>()));

        // Register the DelegatingHandler as transient (required by IHttpClientFactory)
        services.AddTransient<LlmLoggingDelegatingHandler>(sp =>
            new LlmLoggingDelegatingHandler(
                sp.GetRequiredService<ILlmExchangeLogger>(),
                sp.GetRequiredService<ILogger<LlmLoggingDelegatingHandler>>(),
                options));

        // Inject the handler into ALL HttpClient instances created by IHttpClientFactory.
        // This ensures every LLM provider (OpenAI, Anthropic, Ollama, etc.) is automatically
        // intercepted without needing per-provider registration.
        services.ConfigureHttpClientDefaults(builder =>
        {
            builder.AddHttpMessageHandler<LlmLoggingDelegatingHandler>();
        });

        return services;
    }

    /// <summary>
    /// Adds the <see cref="LlmLoggingDelegatingHandler"/> to a specific named HTTP client.
    /// Use this instead of the global <c>ConfigureHttpClientDefaults</c> approach
    /// when you want to log only specific HTTP clients.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <returns>The HTTP client builder for chaining.</returns>
    public static IHttpClientBuilder AddLlmExchangeLogging(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddHttpMessageHandler<LlmLoggingDelegatingHandler>();
    }

    /// <summary>
    /// Registers only the JSON file logger (no structured console logging).
    /// Useful when you want full capture without console noise.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="logDirectory">Directory for .jsonl exchange logs.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLlmExchangeFileLogging(
        this IServiceCollection services,
        string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        services.TryAddSingleton<ILlmExchangeLogger>(sp =>
            new LlmExchangeJsonLogger(
                sp.GetRequiredService<IFileSystemService>(),
                logDirectory,
                sp.GetRequiredService<ILogger<LlmExchangeJsonLogger>>()));

        services.AddTransient<LlmLoggingDelegatingHandler>(sp =>
            new LlmLoggingDelegatingHandler(
                sp.GetRequiredService<ILlmExchangeLogger>(),
                sp.GetRequiredService<ILogger<LlmLoggingDelegatingHandler>>()));

        services.ConfigureHttpClientDefaults(builder =>
        {
            builder.AddHttpMessageHandler<LlmLoggingDelegatingHandler>();
        });

        return services;
    }
}
