namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Resolves a profile name (<c>fast</c>, <c>balanced</c>, <c>quality</c>, or
/// <c>default</c>) to the <see cref="IRagPipeline"/> to exercise. The default
/// implementation (<c>ProfileRagPipelineResolver</c> in <c>Orkeon.Rag</c>,
/// RAG-04/C4) builds and memoizes one pipeline per profile from the
/// <c>RagProfilePresets</c> plus configuration overrides; <c>default</c> maps to
/// the host's registered pipeline.
/// </summary>
public interface IRagProfileResolver
{
    /// <summary>
    /// Returns the pipeline for <paramref name="profileName"/>. Unknown names
    /// fail loudly with the list of known profiles — never a silent fallback.
    /// </summary>
    IRagPipeline Resolve(string profileName);
}
