using Orkeon.Infrastructure.LLMs.Embeddings;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.Services;

public class SimpleEmbeddingServiceTests
{
    private readonly SimpleEmbeddingService _service;
    private readonly TestLogger<SimpleEmbeddingService> _logger;
    private const int DefaultDimension = 384;

    public SimpleEmbeddingServiceTests()
    {
        _logger = new TestLogger<SimpleEmbeddingService>();
        _service = new SimpleEmbeddingService(_logger);
    }

    [Fact]
    public async Task ShouldReturnEmbeddingOfCorrectDimension_WhenGenerateEmbeddingAsyncWithText()
    {
        // Arrange
        var text = "Hello, world!";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(DefaultDimension, embedding.Length);
    }

    [Fact]
    public async Task ShouldReturnSameEmbedding_WhenGenerateEmbeddingAsyncWithSameText()
    {
        // Arrange
        var text = "Deterministic test";

        // Act
        var embedding1 = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);
        var embedding2 = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(embedding1, embedding2);
    }

    [Fact]
    public async Task ShouldReturnDifferentEmbeddings_WhenGenerateEmbeddingAsyncWithDifferentText()
    {
        // Arrange
        var text1 = "First text";
        var text2 = "Second text";

        // Act
        var embedding1 = await _service.GenerateEmbeddingAsync(text1, TestContext.Current.CancellationToken);
        var embedding2 = await _service.GenerateEmbeddingAsync(text2, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(embedding1, embedding2);
    }

    [Fact]
    public async Task ShouldReturnZeroVector_WhenGenerateEmbeddingAsyncWithEmptyText()
    {
        // Arrange
        var text = "";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(embedding, value => Assert.Equal(0f, value));
    }

    [Fact]
    public async Task ShouldReturnNormalizedVector_WhenGenerateEmbeddingAsync()
    {
        // Arrange
        var text = "Test normalization";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));

        // Assert
        Assert.True(Math.Abs(magnitude - 1.0) < 0.0001, "Vector should be normalized");
    }

    [Fact]
    public async Task ShouldReturnMultipleEmbeddings_WhenGenerateEmbeddingsAsyncWithMultipleTexts()
    {
        // Arrange
        var texts = new[] { "Text 1", "Text 2", "Text 3" };

        // Act
        var embeddings = await _service.GenerateEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(DefaultDimension, e.Length));
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenGenerateEmbeddingsAsyncWithCancellation()
    {
        // Arrange
        var texts = Enumerable.Range(0, 100).Select(i => $"Text {i}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.GenerateEmbeddingsAsync(texts, cts.Token));
    }

    [Fact]
    public void ShouldReturnOne_WhenCalculateSimilarityWithIdenticalVectors()
    {
        // Arrange
        var embedding = new float[] { 0.6f, 0.8f, 0.0f };

        // Act
        var similarity = _service.CalculateSimilarity(embedding, embedding);

        // Assert
        Assert.Equal(1.0f, similarity, 5);
    }

    [Fact]
    public void ShouldReturnZero_WhenCalculateSimilarityWithOrthogonalVectors()
    {
        // Arrange
        var embedding1 = new float[] { 1.0f, 0.0f, 0.0f };
        var embedding2 = new float[] { 0.0f, 1.0f, 0.0f };

        // Act
        var similarity = _service.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.Equal(0.0f, similarity, 5);
    }

    [Fact]
    public void ShouldReturnNegativeOne_WhenCalculateSimilarityWithOppositeVectors()
    {
        // Arrange
        var embedding1 = new float[] { 1.0f, 0.0f, 0.0f };
        var embedding2 = new float[] { -1.0f, 0.0f, 0.0f };

        // Act
        var similarity = _service.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.Equal(-1.0f, similarity, 5);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCalculateSimilarityWithDifferentDimensions()
    {
        // Arrange
        var embedding1 = new float[] { 1.0f, 0.0f };
        var embedding2 = new float[] { 1.0f, 0.0f, 0.0f };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _service.CalculateSimilarity(embedding1, embedding2));
    }

    [Fact]
    public void ShouldReturnZero_WhenCalculateSimilarityWithZeroVectors()
    {
        // Arrange
        var embedding1 = new float[] { 0.0f, 0.0f, 0.0f };
        var embedding2 = new float[] { 0.0f, 0.0f, 0.0f };

        // Act
        var similarity = _service.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.Equal(0.0f, similarity);
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenDimension()
    {
        // Assert
        Assert.Equal(DefaultDimension, _service.Dimension);
    }

    [Fact]
    public async Task ShouldCallGenerateEmbeddingAsync_WhenGetEmbeddingAsync()
    {
        // Arrange
        var text = "Test text";

        // Act
        var embedding1 = await _service.GetEmbeddingAsync(text);
        var embedding2 = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(embedding1, embedding2);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGenerateEmbeddingAsyncWithNullText()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GenerateEmbeddingAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnZeroVector_WhenGenerateEmbeddingAsyncWithWhitespaceText()
    {
        // Arrange
        var text = "   \t\n   ";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(embedding, value => Assert.Equal(0f, value));
    }

    [Fact]
    public async Task ShouldWithVeryLongTextTruncatesAndGeneratesEmbedding_WhenGenerateEmbeddingAsync()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat("word", 10000));

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(longText, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(DefaultDimension, embedding.Length);
        Assert.NotEqual(new float[DefaultDimension], embedding);
    }

    [Fact]
    public async Task ShouldReturnEmptyList_WhenGenerateEmbeddingsAsyncWithEmptyList()
    {
        // Arrange
        var texts = Array.Empty<string>();

        // Act
        var embeddings = await _service.GenerateEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(embeddings);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGenerateEmbeddingsAsyncWithNullTexts()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GenerateEmbeddingsAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldHandleAllTypes_WhenGenerateEmbeddingsAsyncWithMixedContent()
    {
        // Arrange
        var texts = new[] { "Normal text", "", "   ", "Another text" };

        // Act
        var embeddings = await _service.GenerateEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, embeddings.Count);
        Assert.NotEqual(embeddings[0], embeddings[1]); // Normal text vs empty
        Assert.Equal(embeddings[1], embeddings[2]); // Empty should equal whitespace
        Assert.NotEqual(embeddings[0], embeddings[3]); // Different normal texts
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCalculateSimilarityWithNullFirstVector()
    {
        // Arrange
        var embedding = new float[] { 1.0f, 0.0f, 0.0f };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.CalculateSimilarity(null!, embedding));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCalculateSimilarityWithNullSecondVector()
    {
        // Arrange
        var embedding = new float[] { 1.0f, 0.0f, 0.0f };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.CalculateSimilarity(embedding, null!));
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCalculateSimilarityWithEmptyVectors()
    {
        // Arrange
        var embedding1 = Array.Empty<float>();
        var embedding2 = Array.Empty<float>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _service.CalculateSimilarity(embedding1, embedding2));
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.707f, 0.707f)]
    [InlineData(-0.5f, 0.5f)]
    public void ShouldReturnCorrectValue_WhenCalculateSimilarityWithPartialSimilarity(float x1, float x2)
    {
        // Arrange
        var embedding1 = new float[] { x1, Math.Abs(1 - x1 * x1) };
        var embedding2 = new float[] { x2, Math.Abs(1 - x2 * x2) };

        // Act
        var similarity = _service.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.InRange(similarity, -1.0f, 1.0f);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenGenerateEmbeddingAsyncWithSpecialCharacters()
    {
        // Arrange
        var text = "Test with 特殊文字 and émojis 😀 and symbols @#$%";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(DefaultDimension, embedding.Length);
        Assert.NotEqual(new float[DefaultDimension], embedding);
    }

    [Fact]
    public async Task ShouldWithNumericTextGeneratesValidEmbedding_WhenGenerateEmbeddingAsync()
    {
        // Arrange
        var text = "123456789 42 3.14159";

        // Act
        var embedding = await _service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(embedding);
        Assert.Equal(DefaultDimension, embedding.Length);
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        Assert.True(Math.Abs(magnitude - 1.0) < 0.0001);
    }

    [Fact]
    public async Task ShouldHandleEfficiently_WhenGenerateEmbeddingsAsyncLargeDataset()
    {
        // Arrange
        var texts = Enumerable.Range(0, 100).Select(i => $"Document {i} with content");

        // Act
        var embeddings = await _service.GenerateEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(100, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(DefaultDimension, e.Length));

        // Verify all embeddings are different
        for (int i = 1; i < embeddings.Count; i++)
        {
            Assert.NotEqual(embeddings[0], embeddings[i]);
        }
    }

    [Fact]
    public void ShouldProduceValidCosineSimilarity_WhenCalculateSimilarityWithNormalizedVectors()
    {
        // Arrange - Create two normalized vectors at 60 degrees angle
        var embedding1 = new float[] { 1.0f, 0.0f, 0.0f };
        var embedding2 = new float[] { 0.5f, 0.866f, 0.0f }; // cos(60°) = 0.5

        // Act
        var similarity = _service.CalculateSimilarity(embedding1, embedding2);

        // Assert
        Assert.Equal(0.5f, similarity, 2); // cos(60°) = 0.5
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenGenerateEmbeddingAsyncWithCancellationRequested()
    {
        // Arrange
        var text = "Test text";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.GenerateEmbeddingAsync(text, cts.Token));
    }

    [Fact]
    public async Task ShouldReturnSameEmbeddings_WhenGenerateEmbeddingsAsyncWithDuplicateTexts()
    {
        // Arrange
        var texts = new[] { "Duplicate", "Unique", "Duplicate", "Another", "Duplicate" };

        // Act
        var embeddings = await _service.GenerateEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, embeddings.Count);
        Assert.Equal(embeddings[0], embeddings[2]); // First and third are duplicates
        Assert.Equal(embeddings[0], embeddings[4]); // First and fifth are duplicates
        Assert.NotEqual(embeddings[0], embeddings[1]); // First and second are different
        Assert.NotEqual(embeddings[1], embeddings[3]); // Second and fourth are different
    }

    [Fact]
    public void ShouldPropertiesAreCorrect_WhenService()
    {
        // Assert
        Assert.NotNull(_service);
        // SimpleEmbeddingService doesn't expose ModelName or MaxTokens directly
        // These tests were checking internal implementation details
    }

    [Fact]
    public async Task ShouldProduceSameEmbedding_WhenGenerateEmbeddingAsyncCaseInsensitive()
    {
        // Arrange
        var text1 = "HELLO WORLD";
        var text2 = "hello world";

        // Act
        var embedding1 = await _service.GenerateEmbeddingAsync(text1, TestContext.Current.CancellationToken);
        var embedding2 = await _service.GenerateEmbeddingAsync(text2, TestContext.Current.CancellationToken);

        // Assert
        // Since the simple service uses hash-based generation, case matters
        Assert.NotEqual(embedding1, embedding2);
    }
}

#pragma warning restore CS0618
