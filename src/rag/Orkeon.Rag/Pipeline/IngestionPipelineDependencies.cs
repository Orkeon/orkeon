using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// The collaborators <see cref="DefaultIngestionPipeline"/> drives, one per stage of
/// the ingestion path (load → validate → chunk → embed → upsert, plus the incremental
/// manifest). They travel together because the pipeline needs all of them, so they are
/// declared once here instead of stretching the constructor signature.
/// </summary>
public sealed record IngestionPipelineDependencies
{
    /// <summary>Resolves the loader able to handle each source.</summary>
    public required DocumentLoaderFactory LoaderFactory { get; init; }

    /// <summary>Resolves the chunking strategy by name.</summary>
    public required ChunkingStrategyFactory ChunkingFactory { get; init; }

    /// <summary>Application embedding port used to embed chunk contents.</summary>
    public required IEmbeddingProvider EmbeddingProvider { get; init; }

    /// <summary>Target document store.</summary>
    public required IDocumentStore Store { get; init; }

    /// <summary>Ingestion-path security validation (anti-injection, provenance, quarantine).</summary>
    public required DataValidationPipeline Validation { get; init; }

    /// <summary>Per-collection ingestion manifest persistence (incremental state).</summary>
    public required IIngestionManifestStore ManifestStore { get; init; }
}
