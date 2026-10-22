using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Builds the backend of the <c>rag.*</c> scripting namespace from a host's services. The pipelines are
/// resolved from the container, never constructed: the ingestion pipeline's per-collection lock lives in
/// its instance, so the REPL, the daemon and <c>rag_ingest</c> must share the DI singleton.
/// </summary>
public static class RagScriptingBackendFactory
{
    /// <summary>
    /// Resolves the RAG pipelines from <paramref name="services"/>; null when the host has none.
    /// Resolution is best-effort: a host without embedding/chat defaults must not break scripts that
    /// never touch <c>rag.*</c> (the binding itself fails loudly on use when null).
    /// </summary>
    /// <param name="services">The host's service provider.</param>
    /// <returns>The backend, or null when the ingestion or retrieval pipeline cannot be resolved.</returns>
    public static RagScriptingBackend? Build(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var ingestionPipeline = TryGet<IIngestionPipeline>(services);
        var ragPipeline = TryGet<IRagPipeline>(services);
        var fileSystem = TryGet<IFileSystemService>(services);
        if (ingestionPipeline is null || ragPipeline is null || fileSystem is null)
            return null;

        return new RagScriptingBackend
        {
            IngestionPipeline = ingestionPipeline,
            RagPipeline = ragPipeline,
            FileSystem = fileSystem,
            // Lets `rag.query({ profile: "corrective" })` select a pipeline per call. Absent resolver:
            // the binding refuses a profile request loudly rather than silently serving the default.
            ProfileResolver = TryGet<IRagProfileResolver>(services),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort optional resolution: any activation failure degrades to 'service unavailable' (the scripting binding fails loudly on use), never crashes scripts that don't use it.")]
    private static T? TryGet<T>(IServiceProvider services) where T : class
    {
        try
        {
            return services.GetService<T>();
        }
        catch (Exception ex)
        {
            LogUnavailable(services, typeof(T), ex);
            return null;
        }
    }

    private static readonly Action<ILogger, string, string, Exception?> OptionalServiceUnavailable =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1, nameof(OptionalServiceUnavailable)), "Optional service {ServiceType} unavailable: {Message}");

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Logging the cause must never break the best-effort resolution.")]
    private static void LogUnavailable(IServiceProvider services, Type serviceType, Exception cause)
    {
        try
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(RagScriptingBackendFactory).FullName!);
            if (logger is not null)
                OptionalServiceUnavailable(logger, serviceType.Name, cause.Message, null);
        }
        catch (Exception)
        {
            // The logger itself is unavailable: nothing more to report.
        }
    }
}
