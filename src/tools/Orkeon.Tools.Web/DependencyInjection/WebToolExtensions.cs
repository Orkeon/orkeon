using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Security;


namespace Orkeon.Tools.Web.DependencyInjection;

/// <summary>
/// Extension methods for registering web tools.
/// </summary>
public static class WebToolExtensions
{
    /// <summary>
    /// Name of the <see cref="HttpClient"/> every web tool goes through when an
    /// <see cref="IHttpClientFactory"/> is available.
    /// </summary>
    public const string HttpClientName = "orkeon.web-tools";

    /// <summary>
    /// Adds web tools (HttpApi, WebScrape, GitHub) to the service collection.
    /// When <see cref="IUrlValidator"/> and <see cref="HttpHeaderSanitizer"/> are registered,
    /// HttpApiTool will use them for SSRF protection and header sanitization.
    /// Use <see cref="AddOrkeonWebSearchTool"/> to register the WebSearch tool separately (requires ISecretProvider).
    /// For Slack tools, use <see cref="AddOrkeonSlackTool"/> and <see cref="AddOrkeonSlackReadTool"/>.
    /// </summary>
    public static IServiceCollection AddOrkeonWebTools(this IServiceCollection services)
    {
        RegisterWebToolsHttpClient(services);
        services.AddTransient<IBaseTool>(CreateHttpApiTool);
        // WebScrapeTool: prefers the RAG-enabled ctor when IEmbeddingService and
        // IMemoryProvider are available. Always wires the IUrlValidator (SSRF) when
        // registered. Falls back gracefully so crews without memory/embeddings still
        // get the basic scrape behavior — but never lose SSRF protection (the
        // fail-closed default guard applies even when no IUrlValidator is registered).
        services.AddTransient<IBaseTool>(CreateWebScrapeTool);
        services.AddTransient<IBaseTool>(CreateScrapeElementTool);
        services.AddTransient<IBaseTool>(CreateGitHubTool);
        services.AddTransient<IBaseTool>(CreateImageGenerationTool);
        return services;
    }

    /// <summary>
    /// Registers the single <see cref="HttpClient"/> every web tool goes through.
    /// Its primary handler refuses redirects: a 302 answered by a host the
    /// <see cref="IUrlValidator"/> just approved would otherwise carry the request
    /// to an internal address nobody validated (SSRF by redirect). A tool that
    /// needs to follow a redirect must re-validate the Location and re-issue the
    /// request itself. PooledConnectionLifetime recycles pooled connections so DNS
    /// changes are honoured. The client is named rather than global on purpose: a
    /// library must not reconfigure the host's own clients.
    /// </summary>
    private static void RegisterWebToolsHttpClient(IServiceCollection services)
    {
        // Every entry point that hands the client to a tool calls this, so the
        // marker keeps the primary-handler configuration registered exactly once:
        // a second ConfigurePrimaryHttpMessageHandler would build a handler that
        // is immediately replaced, and never disposed.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(WebToolsHttpClientMarker)))
            return;

        services.AddSingleton(new WebToolsHttpClientMarker());
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateRedirectFreeHandler);
    }

    /// <summary>Marker service proving the named client has been configured.</summary>
    private sealed class WebToolsHttpClientMarker;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The IHttpClientFactory takes ownership of the primary handler it is given and disposes it when the handler chain expires.")]
    private static HttpMessageHandler CreateRedirectFreeHandler() =>
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

    /// <summary>
    /// Resolves the redirect-free named client. Without an
    /// <see cref="IHttpClientFactory"/> in the container there is nothing safe to
    /// hand over, so the tools fall back to their own static client — which is
    /// configured the same way (see <c>HttpToolBase.CreateSharedHttpClient</c>).
    /// </summary>
    private static HttpClient? ResolveHttpClient(IServiceProvider sp) =>
        sp.GetService<IHttpClientFactory>()?.CreateClient(HttpClientName);

    private static IBaseTool CreateGitHubTool(IServiceProvider sp) =>
        new GitHubTool(
            personalAccessToken: null,
            ResolveHttpClient(sp),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<GitHubTool>>());

    private static IBaseTool CreateHttpApiTool(IServiceProvider sp)
    {
        var urlValidator = sp.GetService<IUrlValidator>();
        var headerSanitizer = sp.GetService<HttpHeaderSanitizer>();
        var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<HttpApiTool>>();
        var httpClient = ResolveHttpClient(sp);

        if (urlValidator is not null && headerSanitizer is not null)
            return new HttpApiTool(urlValidator, headerSanitizer, httpClient, logger);

        return new HttpApiTool(httpClient, logger);
    }

    private static IBaseTool CreateWebScrapeTool(IServiceProvider sp)
    {
        var httpClient = ResolveHttpClient(sp);
        var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<WebScrapeTool>>();
        var embedding = sp.GetService<IEmbeddingService>();
        var memory = sp.GetService<IMemoryProvider>();
        var urlValidator = sp.GetService<IUrlValidator>();
        var headerSanitizer = sp.GetService<HttpHeaderSanitizer>();

        if (embedding is not null && memory is not null)
        {
            return urlValidator is not null && headerSanitizer is not null
                ? new WebScrapeTool(embedding, memory, urlValidator, headerSanitizer, httpClient, logger)
                : new WebScrapeTool(embedding, memory, httpClient, logger);
        }

        return urlValidator is not null && headerSanitizer is not null
            ? new WebScrapeTool(urlValidator, headerSanitizer, httpClient, logger)
            : new WebScrapeTool(httpClient, logger);
    }

    private static IBaseTool CreateScrapeElementTool(IServiceProvider sp)
    {
        var httpClient = ResolveHttpClient(sp);
        var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<ScrapeElementTool>>();
        var urlValidator = sp.GetService<IUrlValidator>();
        var headerSanitizer = sp.GetService<HttpHeaderSanitizer>();

        return urlValidator is not null && headerSanitizer is not null
            ? new ScrapeElementTool(urlValidator, headerSanitizer, httpClient, logger)
            : new ScrapeElementTool(httpClient, logger);
    }

    private static IBaseTool CreateImageGenerationTool(IServiceProvider sp)
    {
        var httpClient = ResolveHttpClient(sp);
        var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<ImageGenerationTool>>();
        var fileSystem = sp.GetService<Orkeon.Domain.FileSystem.IFileSystemService>();
        var urlValidator = sp.GetService<IUrlValidator>();
        var headerSanitizer = sp.GetService<HttpHeaderSanitizer>();

        if (fileSystem is not null && urlValidator is not null && headerSanitizer is not null)
            return new ImageGenerationTool(fileSystem, urlValidator, headerSanitizer, httpClient, logger);

        if (fileSystem is null)
            throw new InvalidOperationException(
                $"{nameof(ImageGenerationTool)} requires an {nameof(Orkeon.Domain.FileSystem.IFileSystemService)} to be registered.");

        return new ImageGenerationTool(fileSystem, httpClient, logger);
    }

    /// <summary>
    /// Adds the WebSearch tool (Tavily provider) to the service collection.
    /// The Tavily API key is retrieved at execution time via the registered <see cref="ISecretProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddOrkeonWebSearchTool(this IServiceCollection services)
    {
        services.AddTransient<IBaseTool>(sp =>
        {
            var secretProvider = sp.GetRequiredService<ISecretProvider>();
            return new WebSearchTool(secretProvider);
        });
        return services;
    }

    /// <summary>
    /// Adds the <see cref="BraveSearchTool"/> to the service collection.
    /// Requires a Brave Search API key (obtain one at https://brave.com/search/api/).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="braveApiKey">The Brave Search API key (X-Subscription-Token).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonBraveSearchTool(
        this IServiceCollection services, string braveApiKey)
    {
        RegisterWebToolsHttpClient(services);
        services.AddTransient<IBaseTool>(sp =>
            new BraveSearchTool(braveApiKey, ResolveHttpClient(sp)));
        return services;
    }

    /// <summary>
    /// Adds the <see cref="SlackTool"/> to the service collection.
    /// Requires a Slack bot token (xoxb-).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="slackBotToken">The Slack bot token (xoxb-) for authentication.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonSlackTool(
        this IServiceCollection services, string slackBotToken)
    {
        RegisterWebToolsHttpClient(services);
        services.AddTransient<IBaseTool>(sp =>
            new SlackTool(slackBotToken, ResolveHttpClient(sp)));
        return services;
    }

    /// <summary>
    /// Adds the <see cref="SlackReadTool"/> to the service collection.
    /// Requires a Slack bot token (xoxb-).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="slackBotToken">The Slack bot token (xoxb-) for authentication.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonSlackReadTool(
        this IServiceCollection services, string slackBotToken)
    {
        RegisterWebToolsHttpClient(services);
        services.AddTransient<IBaseTool>(sp =>
            new SlackReadTool(slackBotToken, ResolveHttpClient(sp)));
        return services;
    }

    /// <summary>
    /// Adds the generic <see cref="CacheSearchTool"/> (<c>cache_search</c>) which lets
    /// agents semantically query content previously stored in the RAG cache by other
    /// tools (e.g. <c>web_scrape</c> with <c>cached=true</c>). Requires
    /// <see cref="IEmbeddingService"/> and <see cref="IMemoryProvider"/> to be
    /// registered in the container — typically by <c>AddOrkeonInfrastructure()</c>.
    /// </summary>
    public static IServiceCollection AddOrkeonCacheSearchTool(this IServiceCollection services)
    {
        services.AddTransient<IBaseTool>(sp =>
        {
            var embedding = sp.GetRequiredService<IEmbeddingService>();
            var memory = sp.GetRequiredService<IMemoryProvider>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<CacheSearchTool>>();
            return new CacheSearchTool(embedding, memory, logger);
        });
        return services;
    }
}
