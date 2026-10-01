using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IEmbeddingProvider"/>: deterministic bag-of-words vectors, so the
/// cosine of two texts follows their word overlap. Keeps runner tests offline and keeps the
/// on-device ONNX model out of them.
/// </summary>
public sealed class LexicalEmbeddingProvider : IEmbeddingProvider
{
    public string Name => "bag-of-words-test";
    public string Model => "bag-of-words";
    public int Dimensions => 256;

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(Embed(text));

    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
        => Task.FromResult<IList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant()
                     .Split([' ', '.', ',', '?', '!', ':', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var slot = 0;
            foreach (var c in word)
                slot = (slot * 31 + c) & 0xFF;
            vector[slot] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }

        return vector;
    }
}
