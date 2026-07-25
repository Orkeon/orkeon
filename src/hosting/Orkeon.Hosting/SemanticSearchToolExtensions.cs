using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tools.Search;
using IEmbeddingService = Orkeon.Application.Interfaces.Ports.IEmbeddingService;

namespace Orkeon.Hosting;

/// <summary>
/// Opt-in DI registration for the <c>semantic_search</c> agent tool.
/// </summary>
/// <remarks>
/// The core <c>Orkeon.Infrastructure.AddOrkeonInfrastructure()</c> does NOT
/// register <see cref="SearchTool"/>: its two ports (<see cref="IEmbeddingService"/>
/// and <see cref="IVectorMemoryStore"/>) require a deliberate choice of
/// provider that depends on the host's appsettings. Calling this extension
/// makes that choice explicit (Application-port <see cref="IEmbeddingProvider"/>
/// adapted to <see cref="IEmbeddingService"/>, plus an
/// <see cref="InMemoryVectorStore"/>).
///
/// Without this call, agents that list <c>semantic_search</c> in their YAML
/// tool list will silently lose it at crew load with the log line
/// <c>"Tool 'semantic_search' not found in registry; skipping."</c>.
///
/// Registered on the shared runner's <see cref="RunnerExecution.RunOneShotAsync"/>
/// configureServices callback (the <c>orkeon run</c> YAML path and the trading /
/// interview-spec-forge runners) so crews that reference it resolve it.
///
/// Embedding resolution note (RAG-01/C4) : the hash-based stub is no longer the
/// implicit default. The Application-port <see cref="IEmbeddingProvider"/> resolves
/// semantic-first (see <c>DefaultEmbeddingProviderResolver</c> in Infrastructure) :
/// local BGE-micro-v2 when <c>AddOrkeonLocalEmbeddings()</c> was called, else the
/// remote provider from the <c>Orkeon:Embeddings</c> configuration, else a fail-fast
/// provider whose first embed call throws an actionable
/// <see cref="InvalidOperationException"/> (« aucun embedding provider sémantique
/// configuré ; ajoutez AddOrkeonLocalEmbeddings() ou configurez Orkeon:Embeddings »).
/// Hosts wanting deterministic non-semantic embeddings (tests) must register
/// <c>Stubs.HashBasedEmbeddingProvider</c> explicitly.
/// </remarks>
public static class SemanticSearchToolExtensions
{
    /// <summary>
    /// Registers <see cref="SearchTool"/> as an <see cref="IBaseTool"/> plus its
    /// two ports : <see cref="IEmbeddingService"/> (adapted from
    /// <see cref="IEmbeddingProvider"/>) and <see cref="IVectorMemoryStore"/>
    /// (in-memory).
    /// </summary>
    public static IServiceCollection AddSemanticSearchTool(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEmbeddingService, EmbeddingServiceAdapter>();
        services.TryAddSingleton<IVectorMemoryStore, InMemoryVectorStore>();

        // Multiple IBaseTool implementations cohabit in DI, stored as a list.
        // AddSingleton is intentional — TryAddSingleton would silently drop us
        // when other IBaseTool registrations already exist.
        services.AddSingleton<IBaseTool, SearchTool>();

        return services;
    }
}

/// <summary>
/// Thin adapter that exposes the Application-port <see cref="IEmbeddingProvider"/>
/// as <see cref="IEmbeddingService"/> (the interface <see cref="SearchTool"/>
/// depends on). Both interfaces live in the same Application namespace but
/// were never bridged — this adapter does it.
/// </summary>
internal sealed partial class EmbeddingServiceAdapter : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;
    private readonly ILogger<EmbeddingServiceAdapter>? _logger;

    public EmbeddingServiceAdapter(IEmbeddingProvider provider, ILogger<EmbeddingServiceAdapter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _logger = logger;
    }

    public async System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text)
    {
        var vec = await _provider.GetEmbeddingAsync(text ?? string.Empty, CancellationToken.None)
            .ConfigureAwait(false);
        if (vec is null || vec.Length == 0)
        {
            if (_logger is not null)
                LogNoVector(_logger, text?.Length ?? 0);
            return Array.Empty<float>();
        }
        return vec;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "IEmbeddingProvider returned no vector for input length={Len}")]
    private static partial void LogNoVector(ILogger logger, int len);
}
