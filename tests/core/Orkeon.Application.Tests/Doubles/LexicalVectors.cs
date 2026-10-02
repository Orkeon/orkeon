namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Deterministic bag-of-words vectors, for <see cref="MockEmbeddingProvider.SetEmbeddingFunc"/>: the
/// cosine of two texts follows their word overlap — the same task in two runs is close, an unrelated
/// one is not. Keeps the memory tests offline and the on-device model out of them.
/// </summary>
internal static class LexicalVectors
{
    public const int Dimensions = 256;

    private static readonly char[] Separators = [' ', '.', ',', '?', '!', ':', ';', '\n', '\r', '-', '(', ')', '"'];

    public static float[] Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var vector = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries))
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
