using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IEmbeddingProvider"/> double: the host normally supplies this port,
/// so a bare <c>ServiceCollection</c> exercising <c>AddSemanticSearchTool</c> must register one
/// for the <c>semantic_search</c> tool graph (SearchTool → EmbeddingServiceAdapter → provider) to
/// resolve. Returns fixed-length zero vectors — enough to construct and enumerate the tool.
/// </summary>
internal sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    public string Name => "stub";
    public string Model => "stub-model";
    public int Dimensions => 8;

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(new float[Dimensions]);

    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return Task.FromResult<IList<float[]>>(texts.Select(_ => new float[Dimensions]).ToList());
    }
}
