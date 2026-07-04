using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Domain.Tools;
using DomainEmbeddingService = Orkeon.Domain.Memory.IEmbeddingService;

namespace Orkeon.Tools.FileSystem.DependencyInjection;

/// <summary>
/// Extension methods for registering file system tools.
/// </summary>
public static class FileSystemToolExtensions
{
    /// <summary>
    /// Adds file system tools (FileRead, FileWrite, DirectoryRead, EmailParser, DirectorySearch) to the service collection.
    /// DirectorySearchTool requires an IEmbeddingService; a no-op fallback is added when none is provided.
    /// </summary>
    public static IServiceCollection AddOrkeonFileSystemTools(this IServiceCollection services)
    {
        services.TryAddSingleton<DomainEmbeddingService, NullDomainEmbeddingService>();

        services.AddTransient<IBaseTool, FileReadTool>();
        services.AddTransient<IBaseTool, FileWriteTool>();
        services.AddTransient<IBaseTool, DirectoryReadTool>();
        services.AddTransient<IBaseTool, EmailParserTool>();
        services.AddTransient<IBaseTool, DirectorySearchTool>();
        services.AddTransient<IBaseTool, CountPatternTool>();
        return services;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via DI container (TryAddSingleton<DomainEmbeddingService, NullDomainEmbeddingService>).")]
    private sealed class NullDomainEmbeddingService : DomainEmbeddingService
    {
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "No IEmbeddingService configured. Register a real embedding provider to use DirectorySearchTool.");
    }
}
