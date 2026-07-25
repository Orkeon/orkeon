using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for the Application port
/// <see cref="IEmbeddingProvider"/>. Returns deterministic vectors of
/// <see cref="Dimensions"/> floats (value = 1-based index within the batch, or 1
/// for unary calls) unless <see cref="EmbeddingFunc"/> is set; records every call.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>Provider name exposed on the port.</summary>
    public string Name { get; set; } = "FakeEmbeddingProvider";

    /// <summary>Model identifier exposed on the port.</summary>
    public string Model { get; set; } = "fake-embedding-model";

    /// <summary>Dimension of the generated vectors.</summary>
    public int Dimensions { get; set; } = 4;

    /// <summary>When set, produces the vector for each text.</summary>
    public Func<string, float[]>? EmbeddingFunc { get; set; }

    /// <summary>Texts received by unary calls, in order.</summary>
    public List<string> UnaryCalls { get; } = [];

    /// <summary>Batches received, in order.</summary>
    public List<IList<string>> BatchCalls { get; } = [];

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        UnaryCalls.Add(text);
        return Task.FromResult(Generate(text, 0));
    }

    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        BatchCalls.Add(texts);
        IList<float[]> result = texts.Select(Generate).ToList();
        return Task.FromResult(result);
    }

    private float[] Generate(string text, int index)
    {
        if (EmbeddingFunc is not null)
        {
            return EmbeddingFunc(text);
        }

        var vector = new float[Dimensions];
        Array.Fill(vector, index + 1f);
        return vector;
    }
}
