namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Resolves a profile name (e.g. <c>fast</c>, <c>balanced</c>, <c>quality</c>) to
/// the <see cref="IRagPipeline"/> the evaluation harness must exercise. This is
/// the seam the profile presets (RAG-04/C4) will plug into: until they exist, the
/// default resolver maps EVERY profile name to the single registered pipeline —
/// a documented, deliberate default so <c>--compare fast,balanced,quality</c>
/// already runs (producing identical columns) before the profiles land.
/// </summary>
public interface IRagProfileResolver
{
    /// <summary>
    /// Returns the pipeline for <paramref name="profileName"/>. Implementations
    /// backed by real presets should fail loudly on unknown names; the default
    /// resolver accepts any name.
    /// </summary>
    IRagPipeline Resolve(string profileName);
}
