using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Knowledge.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering RAG data validation services.
/// </summary>
public static class RagValidationExtensions
{
    /// <summary>
    /// Adds RAG data validation services: content integrity, prompt injection detection,
    /// provenance tracking, quarantine store, and the validation pipeline.
    /// </summary>
    public static IServiceCollection AddOrkeonRagValidation(this IServiceCollection services)
    {
        services.TryAddSingleton<IDataValidator, ContentIntegrityValidator>();
        services.AddSingleton<IDataValidator, PromptInjectionDocumentValidator>();
        services.TryAddSingleton<IProvenanceTracker, ProvenanceTracker>();
        services.TryAddSingleton<IQuarantineStore, InMemoryQuarantineStore>();
        services.TryAddSingleton<DataValidationPipeline>();

        return services;
    }
}
