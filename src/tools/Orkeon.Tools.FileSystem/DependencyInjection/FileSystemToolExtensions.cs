using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Rag.DependencyInjection;

namespace Orkeon.Tools.FileSystem.DependencyInjection;

/// <summary>
/// Extension methods for registering file system tools.
/// </summary>
public static class FileSystemToolExtensions
{
    /// <summary>
    /// Adds file system tools (FileRead, FileWrite, DirectoryRead, EmailParser, DirectorySearch) to the service collection.
    /// The directory_search facade rides on the shared ephemeral-collection RAG search
    /// (RAG-03/C5): the host must register the RAG subsystem (<c>AddOrkeonRag</c> + an
    /// embedding provider) for it to run — without it, registration and construction
    /// still succeed and searches fail loudly at call time with an actionable message.
    /// </summary>
    public static IServiceCollection AddOrkeonFileSystemTools(this IServiceCollection services)
    {
        // Shared ephemeral-collection search engine of directory_search (TryAdd, idempotent).
        services.AddOrkeonEphemeralSearch();

        services.AddTransient<IBaseTool, FileReadTool>();
        services.AddTransient<IBaseTool, FileWriteTool>();
        services.AddTransient<IBaseTool, DirectoryReadTool>();
        services.AddTransient<IBaseTool, EmailParserTool>();
        services.AddTransient<IBaseTool, DirectorySearchTool>();
        services.AddTransient<IBaseTool, CountPatternTool>();
        return services;
    }
}
