using Orkeon.Application.Memory;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Services;

public class DomainEmbeddingServiceAdapterTests
{
    #region Test Doubles

    private class TestEmbeddingService : IEmbeddingService
    {
        public List<string> ProcessedTexts { get; } = [];
        public float[] EmbeddingToReturn { get; set; } = [0.1f, 0.2f, 0.3f];
        public bool ThrowException { get; set; }
        public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("Test exception");

        public async System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text)
        {
            ProcessedTexts.Add(text);

            if (ThrowException)
            {
                throw ExceptionToThrow;
            }

            // Simulate async work
            await System.Threading.Tasks.Task.Delay(10);
            return EmbeddingToReturn;
        }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructingWithValidService()
    {
        // Arrange
        var applicationService = new TestEmbeddingService();

        // Act
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);

        // Assert
        Assert.NotNull(adapter);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullService()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DomainEmbeddingServiceAdapter(null!));
        Assert.Equal("applicationService", exception.ParamName);
    }

    #endregion

    #region GetEmbeddingAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmbedding_WhenGettingEmbeddingAsyncWithValidText()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.5f, 0.6f, 0.7f, 0.8f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var text = "This is a test text for embedding";

        // Act
        var result = await adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
        Assert.Equal(0.5f, result[0]);
        Assert.Equal(0.6f, result[1]);
        Assert.Equal(0.7f, result[2]);
        Assert.Equal(0.8f, result[3]);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(text, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPassToApplicationService_WhenGettingEmbeddingAsyncWithEmptyText()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.0f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var emptyText = "";

        // Act
        var result = await adapter.GetEmbeddingAsync(emptyText, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(0.0f, result[0]);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(emptyText, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPassToApplicationService_WhenGettingEmbeddingAsyncWithLongText()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [1.0f, 2.0f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var longText = new string('A', 10000); // Very long text

        // Act
        var result = await adapter.GetEmbeddingAsync(longText, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(1.0f, result[0]);
        Assert.Equal(2.0f, result[1]);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(longText, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPassToApplicationService_WhenGettingEmbeddingAsyncWithSpecialCharacters()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.1f, 0.2f, 0.3f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var specialText = "Text with émojis 🚀 and spëcial chärs: @#$%^&*()";

        // Act
        var result = await adapter.GetEmbeddingAsync(specialText, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(specialText, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillWork_WhenGettingEmbeddingAsyncWithCancellationToken()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.9f, 0.8f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        using var cts = new CancellationTokenSource();
        var text = "Test with cancellation token";

        // Act
        var result = await adapter.GetEmbeddingAsync(text, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(0.9f, result[0]);
        Assert.Equal(0.8f, result[1]);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(text, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillCallApplicationService_WhenGettingEmbeddingAsyncWithCancelledToken()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.5f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel before calling
        var text = "Test with cancelled token";

        // Act - The adapter doesn't use the cancellation token, so it should still work
        var result = await adapter.GetEmbeddingAsync(text, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(0.5f, result[0]);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(text, applicationService.ProcessedTexts[0]);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenGettingEmbeddingAsyncWhenApplicationServiceThrowsException()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            ThrowException = true,
            ExceptionToThrow = new InvalidOperationException("Service unavailable")
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var text = "Test text";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken));
        Assert.Equal("Service unavailable", exception.Message);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(text, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenGettingEmbeddingAsyncWhenApplicationServiceThrowsArgumentException()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            ThrowException = true,
            ExceptionToThrow = new ArgumentException("Invalid text", "text")
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var text = "Invalid text";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken));
        Assert.Contains("Invalid text", exception.Message); // Message includes parameter name
        Assert.Equal("text", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenGettingEmbeddingAsyncWhenApplicationServiceThrowsTaskCanceledException()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            ThrowException = true,
            ExceptionToThrow = new TaskCanceledException("Operation was cancelled")
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var text = "Test text";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(
            () => adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken));
        Assert.Equal("Operation was cancelled", exception.Message);
    }

    #endregion

    #region Multiple Calls Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallApplicationServiceForEach_WhenGettingEmbeddingAsyncWithMultipleCalls()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.1f, 0.2f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var texts = new[] { "First text", "Second text", "Third text" };

        // Act
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            var result = await adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken);
            results.Add(result);
        }

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal(2, result.Length);
            Assert.Equal(0.1f, result[0]);
            Assert.Equal(0.2f, result[1]);
        });
        Assert.Equal(3, applicationService.ProcessedTexts.Count);
        Assert.Equal("First text", applicationService.ProcessedTexts[0]);
        Assert.Equal("Second text", applicationService.ProcessedTexts[1]);
        Assert.Equal("Third text", applicationService.ProcessedTexts[2]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenGettingEmbeddingAsyncWithConcurrentCalls()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.3f, 0.4f, 0.5f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var texts = Enumerable.Range(1, 10).Select(i => $"Text {i}").ToArray();

        // Act
        var tasks = texts.Select(text => adapter.GetEmbeddingAsync(text)).ToArray();
        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, results.Length);
        Assert.All(results, result =>
        {
            Assert.Equal(3, result.Length);
            Assert.Equal(0.3f, result[0]);
            Assert.Equal(0.4f, result[1]);
            Assert.Equal(0.5f, result[2]);
        });
        Assert.Equal(10, applicationService.ProcessedTexts.Count);

        // All texts should be processed (order might vary due to concurrency)
        foreach (var expectedText in texts)
        {
            Assert.Contains(expectedText, applicationService.ProcessedTexts);
        }
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldPassToApplicationService_WhenGettingEmbeddingAsyncWithNullText()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = [0.0f]
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);

        // Act & Assert
        // The adapter doesn't validate input - it passes everything to the application service
        // If the application service throws, that's expected behavior
        try
        {
            await adapter.GetEmbeddingAsync(null!, TestContext.Current.CancellationToken);
        }
        catch
        {
            // Exception handling is tested elsewhere
        }

        Assert.Single(applicationService.ProcessedTexts);
        Assert.Null(applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNull_WhenGettingEmbeddingAsyncWhenApplicationServiceReturnsNull()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = null!
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var text = "Test text";

        // Act
        var result = await adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(text, applicationService.ProcessedTexts[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyArray_WhenGettingEmbeddingAsyncWhenApplicationServiceReturnsEmptyArray()
    {
        // Arrange
        var applicationService = new TestEmbeddingService
        {
            EmbeddingToReturn = []
        };
        var adapter = new DomainEmbeddingServiceAdapter(applicationService);
        var text = "Test text";

        // Act
        var result = await adapter.GetEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.Single(applicationService.ProcessedTexts);
        Assert.Equal(text, applicationService.ProcessedTexts[0]);
    }

    #endregion
}
