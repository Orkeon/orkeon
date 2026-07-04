namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// ONNX-free guard tests for <see cref="LocalEmbeddingGenerator"/>. The constructor's
/// <c>ArgumentNullException.ThrowIfNull(inner)</c> runs before any provider interaction,
/// so this path is exercisable without booting the embedded model. The remaining members
/// (<c>GenerateAsync</c>, <c>GetService</c>, <c>Dispose</c>) require a live
/// <see cref="LocalEmbeddingProvider"/> and are covered by the Slow-tagged suite.
/// </summary>
public sealed class LocalEmbeddingGeneratorGuardTests
{
    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalEmbeddingGenerator(null!));
    }
}
