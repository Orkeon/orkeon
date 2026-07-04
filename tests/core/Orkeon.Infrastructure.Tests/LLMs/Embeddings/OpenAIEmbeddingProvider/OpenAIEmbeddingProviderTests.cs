using Orkeon.Application.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public sealed class OpenAIEmbeddingProviderTests : IDisposable
{
    private readonly MockEmbeddingGenerator _mockGenerator;
    private readonly EmbeddingOptions _options;
    private readonly OpenAIEmbeddingProvider _provider;

    public OpenAIEmbeddingProviderTests()
    {
        _mockGenerator = new MockEmbeddingGenerator();
        _options = new EmbeddingOptions
        {
            Model = "text-embedding-3-small",
            Dimension = 1536
        };
        _provider = new OpenAIEmbeddingProvider(
            _mockGenerator,
            Options.Create(_options));
    }

    [Fact]
    public async Task ShouldReturnVectorOfCorrectDimension_WhenGetEmbeddingAsync()
    {
        // Arrange
        var expectedVector = new float[] { 0.1f, 0.2f, 0.3f };
        _mockGenerator.SetDefaultVector(expectedVector);

        // Act
        var vector = await _provider.GetEmbeddingAsync("test text", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedVector.Length, vector.Length);
        Assert.Equal(expectedVector[0], vector[0]);
        Assert.Equal(expectedVector[1], vector[1]);
        Assert.Equal(expectedVector[2], vector[2]);
    }

    [Fact]
    public async Task ShouldBatchReturnsCorrectCount_WhenGetEmbeddingsAsync()
    {
        // Arrange
        var vec1 = new float[] { 0.1f, 0.2f };
        var vec2 = new float[] { 0.3f, 0.4f };

        var callCount = 0;
        _mockGenerator.SetGenerateFunc(values =>
        {
            callCount++;
            var list = values.ToList();
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            // Return vec1 for first input, vec2 for second
            for (int i = 0; i < list.Count; i++)
            {
                embeddings.Add(new Embedding<float>(i == 0 ? vec1 : vec2));
            }
            return embeddings;
        });

        // Act
        var results = await _provider.GetEmbeddingsAsync(["text1", "text2"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(vec1[0], results[0][0]);
        Assert.Equal(vec2[0], results[1][0]);
    }

    [Fact]
    public void ShouldReturnOpenAI_WhenName()
    {
        Assert.Equal("OpenAI", _provider.Name);
    }

    [Fact]
    public void ShouldReturnConfiguredModel_WhenModel()
    {
        Assert.Equal("text-embedding-3-small", _provider.Model);
    }

    [Fact]
    public void ShouldReturnConfiguredDimension_WhenDimensions()
    {
        Assert.Equal(1536, _provider.Dimensions);
    }

    public void Dispose()
    {
        _mockGenerator.Dispose();
        GC.SuppressFinalize(this);
    }
}
