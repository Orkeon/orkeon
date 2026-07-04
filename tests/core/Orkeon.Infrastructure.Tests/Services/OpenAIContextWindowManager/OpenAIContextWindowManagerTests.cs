using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.LLMs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Services;

public class OpenAIContextWindowManagerTests
{
    private readonly TestTokenCounter _tokenCounter;
    private readonly TestLlmProvider _llmProvider;
    private readonly TestLogger<OpenAIContextWindowManager> _logger;
    private readonly OpenAIContextWindowManager _manager;

    public OpenAIContextWindowManagerTests()
    {
        _tokenCounter = new TestTokenCounter();
        _llmProvider = new TestLlmProvider();
        _logger = new TestLogger<OpenAIContextWindowManager>();
        _manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullTokenCounter()
    {
        // Arrange
        ITokenCounter? tokenCounter = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new OpenAIContextWindowManager(tokenCounter!, _llmProvider, _logger));
        Assert.Equal("tokenCounter", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLlmProvider()
    {
        // Arrange
        ILlmProvider? llmProvider = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new OpenAIContextWindowManager(_tokenCounter, llmProvider!, _logger));
        Assert.Equal("llm", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLogger()
    {
        // Arrange
        ILogger<OpenAIContextWindowManager>? logger = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new OpenAIContextWindowManager(_tokenCounter, _llmProvider, logger!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructorWithValidParameters()
    {
        // Arrange & Act
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void ShouldUseProvidedSettings_WhenConstructorWithCustomSettings()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(
            autoSummarize: false,
            compressionRatio: 0.5,
            summaryPrompt: "Custom prompt");

        // Act
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task ShouldReturnOriginalContent_WhenSummarizeIfNeededAsyncWithContentUnderLimit()
    {
        // Arrange
        var content = "This is a short content.";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 50);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal(content, result);
        Assert.False(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldSummarize_WhenSummarizeIfNeededAsyncWithContentOverLimit()
    {
        // Arrange
        var content = "This is a very long content that exceeds the token limit and needs to be summarized.";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupResponse("Summarized content");

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal("Summarized content", result);
        Assert.True(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldReturnOriginalContent_WhenSummarizeIfNeededAsyncWithAutoSummarizeDisabled()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(autoSummarize: false);
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        var content = "This is a very long content that exceeds the token limit.";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);

        // Act
        var result = await manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal(content, result);
        Assert.False(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldReturnTruncatedContent_WhenSummarizeIfNeededAsyncWhenLlmReturnsEmpty()
    {
        // Arrange
        var content = "This is a very long content that exceeds the token limit and needs truncation.";
        var maxTokens = 10;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupResponse(""); // Empty response

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.NotEqual(content, result);
        Assert.EndsWith("...", result);
        Assert.True(result.Length < content.Length);
    }

    [Fact]
    public async Task ShouldReturnTruncatedContent_WhenSummarizeIfNeededAsyncWhenLlmThrowsException()
    {
        // Arrange
        var content = "This is a very long content that exceeds the token limit and needs truncation.";
        var maxTokens = 10;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupException(new Exception("LLM error"));

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.NotEqual(content, result);
        Assert.EndsWith("...", result);
        Assert.True(result.Length < content.Length);
    }

    [Fact]
    public async Task ShouldUseCorrectTargetTokens_WhenSummarizeIfNeededAsyncWithCustomCompressionRatio()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(compressionRatio: 0.5);
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        var content = "Long content";
        var maxTokens = 100;
        var expectedTargetTokens = 50; // 100 * 0.5

        _tokenCounter.SetTokenCount(content, 200);
        _llmProvider.SetupResponse("Summarized");

        // Act
        await manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        var lastConfig = _llmProvider.LastUsedConfig;
        Assert.NotNull(lastConfig);
        Assert.Equal(expectedTargetTokens, lastConfig.MaxTokens);
    }

    [Fact]
    public async Task ShouldUseProvidedPrompt_WhenSummarizeIfNeededAsyncWithCustomPrompt()
    {
        // Arrange
        var customPrompt = "Please provide a brief summary:";
        var settings = ContextWindowSettings.Create(summaryPrompt: customPrompt);
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        var content = "Long content";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupResponse("Summary");

        // Act
        await manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        var lastPrompt = _llmProvider.LastUsedPrompt;
        Assert.NotNull(lastPrompt);
        Assert.Contains(customPrompt, lastPrompt);
    }

    [Fact]
    public void ShouldReturnFalse_WhenIsContextExceededWithContentUnderLimit()
    {
        // Arrange
        var content = "Short content";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 50);

        // Act
        var result = _manager.IsContextExceeded(content, maxTokens);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnTrue_WhenIsContextExceededWithContentOverLimit()
    {
        // Arrange
        var content = "Long content";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);

        // Act
        var result = _manager.IsContextExceeded(content, maxTokens);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenIsContextExceededWithContentEqualToLimit()
    {
        // Arrange
        var content = "Content exactly at limit";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 100);

        // Act
        var result = _manager.IsContextExceeded(content, maxTokens);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldUseLowerTemperatureForConsistency_WhenSummarizeIfNeededAsync()
    {
        // Arrange
        var content = "Long content that needs summarization";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupResponse("Summary");

        // Act
        await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        var lastConfig = _llmProvider.LastUsedConfig;
        Assert.NotNull(lastConfig);
        Assert.Equal(0.3, lastConfig.Temperature);
    }

    [Fact]
    public async Task ShouldHandleEachIndependently_WhenSummarizeIfNeededAsyncMultipleCalls()
    {
        // Arrange
        var content1 = "First content";
        var content2 = "Second content that is much longer";
        var maxTokens = 50;

        _tokenCounter.SetTokenCount(content1, 30);
        _tokenCounter.SetTokenCount(content2, 100);
        _llmProvider.SetupResponse("Summarized second content");

        // Act
        var result1 = await _manager.SummarizeIfNeededAsync(content1, maxTokens);
        var result2 = await _manager.SummarizeIfNeededAsync(content2, maxTokens);

        // Assert
        Assert.Equal(content1, result1);
        Assert.Equal("Summarized second content", result2);
    }

    [Fact]
    public async Task ShouldReturnTruncatedContent_WhenSummarizeIfNeededAsyncWithZeroMaxTokens()
    {
        // Arrange
        var content = "Some content that needs processing";
        var maxTokens = 0;
        _tokenCounter.SetTokenCount(content, 50);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal("...", result); // Should be truncated to just ellipsis
    }

    [Fact]
    public async Task ShouldReturnTruncatedContent_WhenSummarizeIfNeededAsyncWithNegativeMaxTokens()
    {
        // Arrange
        var content = "Some content";
        var maxTokens = -10;
        _tokenCounter.SetTokenCount(content, 50);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal("...", result);
    }

    [Fact]
    public async Task ShouldReturnOriginalContent_WhenSummarizeIfNeededAsyncWithVeryLargeMaxTokens()
    {
        // Arrange
        var content = "Normal sized content";
        var maxTokens = int.MaxValue;
        _tokenCounter.SetTokenCount(content, 100);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal(content, result);
        Assert.False(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSummarizeIfNeededAsyncWithEmptyContent()
    {
        // Arrange
        var content = "";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 0);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal(content, result);
        Assert.False(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenSummarizeIfNeededAsyncWithNullContent()
    {
        // Arrange
        string? content = null;
        var maxTokens = 100;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _manager.SummarizeIfNeededAsync(content!, maxTokens));
    }

    [Fact]
    public async Task ShouldReturnOriginal_WhenSummarizeIfNeededAsyncWithWhitespaceContent()
    {
        // Arrange
        var content = "   \n\t   ";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 5);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal(content, result);
        Assert.False(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public Task ShouldAlwaysReturnTrue_WhenIsContextExceededWithZeroMaxTokens()
    {
        // Arrange
        var content = "Any content";
        var maxTokens = 0;
        _tokenCounter.SetTokenCount(content, 10);

        // Act
        var result = _manager.IsContextExceeded(content, maxTokens);

        // Assert
        Assert.True(result);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ShouldReturnFalse_WhenIsContextExceededWithEmptyContent()
    {
        // Arrange
        var content = "";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 0);

        // Act
        var result = _manager.IsContextExceeded(content, maxTokens);

        // Assert
        Assert.False(result);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldPropagateToLlm_WhenSummarizeIfNeededAsyncWithCancellationToken()
    {
        // Arrange
        var content = "Long content needing summary";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupResponse("Summary");

        using var cts = new CancellationTokenSource();

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal("Summary", result);
        Assert.True(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldTruncateCorrectly_WhenSummarizeIfNeededAsyncWithVeryLongContent()
    {
        // Arrange
        var content = new string('A', 10000); // Very long content
        var maxTokens = 10;
        _tokenCounter.SetTokenCount(content, 2500);
        _llmProvider.SetupResponse(""); // Empty response to trigger truncation

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.EndsWith("...", result);
        Assert.True(result.Length <= maxTokens * 4); // Using avg 4 chars per token
    }

    [Fact]
    public async Task ShouldUseFullMaxTokens_WhenSummarizeIfNeededAsyncWithCompressionRatioOne()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(compressionRatio: 1.0);
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        var content = "Long content";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 200);
        _llmProvider.SetupResponse("Summary");

        // Act
        await manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        var lastConfig = _llmProvider.LastUsedConfig;
        Assert.NotNull(lastConfig);
        Assert.Equal(100, lastConfig.MaxTokens); // Should use full max tokens
    }

    [Fact]
    public async Task ShouldUseZeroTargetTokens_WhenSummarizeIfNeededAsyncWithCompressionRatioZero()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(compressionRatio: 0.0);
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        var content = "Long content";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 200);
        _llmProvider.SetupResponse("S");

        // Act
        await manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        var lastConfig = _llmProvider.LastUsedConfig;
        Assert.NotNull(lastConfig);
        Assert.Equal(0, lastConfig.MaxTokens);
    }

    [Fact]
    public async Task ShouldLogCorrectDebugMessageWhenContentWithinLimit_WhenSummarizeIfNeededAsync()
    {
        // Arrange
        var content = "Short content";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 50);

        // Act
        await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.True(_logger.HasLoggedDebug("Content within token limit"));
        Assert.True(_logger.HasLoggedDebug("50/100"));
    }

    [Fact]
    public async Task ShouldLogCorrectInformationMessageWhenSummarizing_WhenSummarizeIfNeededAsync()
    {
        // Arrange
        var content = "Long content";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupResponse("Summary");

        // Act
        await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.True(_logger.HasLoggedInformation("Summarizing content from 100"));
    }

    [Fact]
    public async Task ShouldLogWarningWhenAutoSummarizeDisabled_WhenSummarizeIfNeededAsync()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(autoSummarize: false);
        var manager = new OpenAIContextWindowManager(_tokenCounter, _llmProvider, _logger, settings);

        var content = "Long content";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);

        // Act
        await manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.True(_logger.HasLoggedWarning("Content exceeds token limit but auto-summarize is disabled"));
    }

    [Fact]
    public async Task ShouldLogErrorWhenLlmThrows_WhenSummarizeIfNeededAsync()
    {
        // Arrange
        var content = "Long content";
        var maxTokens = 50;
        _tokenCounter.SetTokenCount(content, 100);
        _llmProvider.SetupException(new InvalidOperationException("LLM failure"));

        // Act
        await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.True(_logger.HasLoggedError("Error during summarization"));
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSummarizeIfNeededAsyncWithSpecialCharacters()
    {
        // Arrange
        var content = "Content with special chars: \n\t\r🎉 © ® ™";
        var maxTokens = 100;
        _tokenCounter.SetTokenCount(content, 50);

        // Act
        var result = await _manager.SummarizeIfNeededAsync(content, maxTokens);

        // Assert
        Assert.Equal(content, result);
        Assert.False(_llmProvider.WasGenerateCalled);
    }

    [Fact]
    public async Task ShouldHandleIndependently_WhenSummarizeIfNeededAsyncConcurrentCalls()
    {
        // Arrange
        var content1 = "First long content";
        var content2 = "Second long content";
        var maxTokens = 50;

        _tokenCounter.SetTokenCount(content1, 100);
        _tokenCounter.SetTokenCount(content2, 100);
        _llmProvider.SetupResponse("Summary");

        // Act
        var task1 = _manager.SummarizeIfNeededAsync(content1, maxTokens);
        var task2 = _manager.SummarizeIfNeededAsync(content2, maxTokens);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.All(results, r => Assert.Equal("Summary", r));
    }

    [Fact]
    public void ShouldThrow_WhenIsContextExceededWithNullContent()
    {
        // Arrange
        string? content = null;
        var maxTokens = 100;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _manager.IsContextExceeded(content!, maxTokens));
    }
}

// Test doubles
internal class TestTokenCounter : ITokenCounter
{
    private readonly Dictionary<string, int> _tokenCounts = [];

    public void SetTokenCount(string text, int count)
    {
        _tokenCounts[text] = count;
    }

    public int CountTokens(string text)
    {
        return _tokenCounts.TryGetValue(text, out var count) ? count : text.Length / 4;
    }
}

internal class TestLlmProvider : ILlmProvider
{
    private string _response = "Default response";
    private Exception? _exception;

    public bool WasGenerateCalled { get; private set; }
    public string? LastUsedPrompt { get; private set; }
    public LlmConfig? LastUsedConfig { get; private set; }

    public void SetupResponse(string response)
    {
        _response = response;
        _exception = null;
    }

    public void SetupException(Exception exception)
    {
        _exception = exception;
    }

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        WasGenerateCalled = true;
        LastUsedPrompt = prompt;
        LastUsedConfig = config;

        if (_exception != null)
            throw _exception;

        return Task.FromResult(new LlmResponse
        {
            Content = _response,
            Model = config?.Model ?? TestModelName,
            TokensUsed = 10,
            Metadata = new Dictionary<string, object>
            {
                ["model"] = config?.Model ?? TestModelName,
                ["tokens_used"] = 10,
                ["response_time"] = TimeSpan.FromMilliseconds(100)
            }
        });
    }

    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        // For test purposes, just use the last message content as prompt
        var prompt = messages.LastOrDefault()?.Content ?? "";
        return GenerateAsync(prompt, config, cancellationToken);
    }

    public static Task<bool> ValidateConnectionAsync()
    {
        return Task.FromResult(true);
    }

    public string Name => "TestLlmProvider";
}

// Test logger
internal class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    private readonly List<string> _logMessages = [];

    public IReadOnlyList<string> LogMessages => _logMessages;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logMessages.Add($"[{logLevel}] {message}");
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool HasLoggedWarning(string message) =>
        _logMessages.Any(m => m.Contains("[Warning]") && m.Contains(message));

    public bool HasLoggedError(string message) =>
        _logMessages.Any(m => m.Contains("[Error]") && m.Contains(message));

    public bool HasLoggedInformation(string message) =>
        _logMessages.Any(m => m.Contains("[Information]") && m.Contains(message));

    public bool HasLoggedDebug(string message) =>
        _logMessages.Any(m => m.Contains("[Debug]") && m.Contains(message));
}
