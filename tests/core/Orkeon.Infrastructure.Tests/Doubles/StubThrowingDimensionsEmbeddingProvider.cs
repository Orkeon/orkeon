using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>An embedding provider whose Dimensions cannot be read, with a distinctive message.</summary>
public sealed class StubThrowingDimensionsEmbeddingProvider : IEmbeddingProvider
{
    public const string FailureMessage = "ch38 probe: no api key configured";

    public string Name => "Throwing";
    public string Model => "throwing-model";
    public int Dimensions => throw new InvalidOperationException(FailureMessage);

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
