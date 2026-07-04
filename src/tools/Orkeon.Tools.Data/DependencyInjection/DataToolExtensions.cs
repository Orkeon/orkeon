using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.MongoDB;
using Orkeon.Tools.Data.Graph;
using Orkeon.Tools.Data.Search;
using DomainEmbeddingService = Orkeon.Domain.Memory.IEmbeddingService;

namespace Orkeon.Tools.Data.DependencyInjection;

/// <summary>
/// Extension methods for registering data processing tools.
/// </summary>
public static class DataToolExtensions
{
    /// <summary>
    /// Adds data tools (JSON, CSV, PDF, XML, Database, MongoDB, Graph) to the service collection.
    /// RAG search tools (TXT, MDX, PDF) require an IEmbeddingService registration;
    /// a no-op fallback is added automatically when none is provided.
    /// </summary>
    public static IServiceCollection AddOrkeonDataTools(this IServiceCollection services)
    {
        // Fallback embedding service for RAG tools — real providers override via TryAdd
        services.TryAddSingleton<DomainEmbeddingService, NullDomainEmbeddingService>();

        services.AddTransient<IBaseTool, JsonTool>();
        services.AddTransient<IBaseTool, CsvReaderTool>();
        services.AddTransient<IBaseTool, PdfReaderTool>();
        services.AddTransient<IBaseTool, XmlParserTool>();
        services.AddTransient<IBaseTool, DocxReadTool>();
        services.AddTransient<IBaseTool, DocxWriteTool>();
        services.AddTransient<IBaseTool, XlsxReadTool>();
        services.AddTransient<IBaseTool, XlsxWriteTool>();
        services.AddTransient<IBaseTool, TxtSearchTool>();
        services.AddTransient<IBaseTool, MdxSearchTool>();
        services.AddTransient<IBaseTool, PdfSearchTool>();
        services.AddRelationalDatabaseTools();
        services.AddMongoDbTools();
        services.AddGraphDatabaseTools();
        return services;
    }

    /// <summary>
    /// Adds relational database tools with provider factory and security policy.
    /// </summary>
    public static IServiceCollection AddRelationalDatabaseTools(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseProviderFactory, DatabaseProviderFactory>();
        services.AddSingleton<IDatabaseSecurityPolicy, DefaultDatabaseSecurityPolicy>();
        services.AddTransient<IBaseTool, RelationalDatabaseTool>();
        services.AddTransient<IBaseTool, SqlServerDatabaseTool>();
        services.AddTransient<IBaseTool, PostgresDatabaseTool>();
        services.AddTransient<IBaseTool, MySqlDatabaseTool>();
        services.AddTransient<IBaseTool, MariaDbDatabaseTool>();
        services.AddTransient<IBaseTool, DatabaseSchemaTool>();
        return services;
    }

    /// <summary>
    /// Adds MongoDB tools (MongoDbTool, MongoDbSchemaTool) to the service collection.
    /// </summary>
    public static IServiceCollection AddMongoDbTools(this IServiceCollection services)
    {
        services.AddTransient<IBaseTool, MongoDbTool>();
        services.AddTransient<IBaseTool, MongoDbSchemaTool>();
        return services;
    }

    /// <summary>
    /// Adds graph database tools (ArcadeDB, JanusGraph, GraphSchema) to the service collection.
    /// </summary>
    public static IServiceCollection AddGraphDatabaseTools(this IServiceCollection services)
    {
        services.AddTransient<IBaseTool, ArcadeDbTool>();
        services.AddTransient<IBaseTool, JanusGraphTool>();
        services.AddTransient<IBaseTool, GraphSchemaTool>();
        return services;
    }

    /// <summary>No-op fallback for Domain IEmbeddingService when no real provider is configured.</summary>
    private sealed class NullDomainEmbeddingService : DomainEmbeddingService
    {
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "No IEmbeddingService configured. Register a real embedding provider to use RAG search tools.");
    }
}
