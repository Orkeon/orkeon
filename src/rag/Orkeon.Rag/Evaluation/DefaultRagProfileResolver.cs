using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Single documented default of <see cref="IRagProfileResolver"/> until the
/// profile presets land (RAG-04/C4): EVERY profile name — <c>default</c>,
/// <c>fast</c>, <c>balanced</c>, <c>quality</c>, anything — resolves to the one
/// registered <see cref="IRagPipeline"/>. This keeps the harness (and
/// <c>--compare</c>) runnable today; a comparison across profiles therefore
/// yields identical rows until a preset-aware resolver replaces this one.
/// </summary>
public sealed class DefaultRagProfileResolver : IRagProfileResolver
{
    private readonly IRagPipeline _pipeline;

    /// <summary>Initializes the resolver over the single registered pipeline.</summary>
    public DefaultRagProfileResolver(IRagPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public IRagPipeline Resolve(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return _pipeline;
    }
}
