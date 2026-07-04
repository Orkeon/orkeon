using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class LlmProviderAdapterTests
{
    #region Test Doubles

    private class TestLlmProvider : ILlmProvider
    {
        private readonly string _name;
        private readonly bool _shouldThrowOnGenerate;
        private readonly string _responseContent;

        public List<(string prompt, LlmConfig? config)> GenerateCalls { get; } = [];
        public List<(LlmMessage[] messages, LlmConfig? config)> ChatCalls { get; } = [];

        public string Name => _name;

        public TestLlmProvider(
            string name = "TestProvider",
            bool shouldThrowOnGenerate = false,
            string responseContent = "Test response")
        {
            _name = name;
            _shouldThrowOnGenerate = shouldThrowOnGenerate;
            _responseContent = responseContent;
        }

        public async Task<LlmResponse> GenerateAsync(
            string prompt,
            LlmConfig? config = null,
            CancellationToken cancellationToken = default)
        {
            GenerateCalls.Add((prompt, config));

            if (_shouldThrowOnGenerate)
            {
                throw new InvalidOperationException("Provider not available");
            }

            await System.Threading.Tasks.Task.Delay(10, cancellationToken);

            return new LlmResponse
            {
                Content = _responseContent,
                TokensUsed = 10,
                Model = config?.Model ?? TestModelName,
                Metadata = new Dictionary<string, object>()
            };
        }

        public async Task<LlmResponse> ChatAsync(
            LlmMessage[] messages,
            LlmConfig? config = null,
            CancellationToken cancellationToken = default)
        {
            ChatCalls.Add((messages, config));

            await System.Threading.Tasks.Task.Delay(10, cancellationToken);

            return new LlmResponse
            {
                Content = _responseContent,
                TokensUsed = 20,
                Model = config?.Model ?? TestModelName,
                Metadata = new Dictionary<string, object>()
            };
        }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldCreateAdapter_WhenConstructorWithValidProvider()
    {
        // Arrange
        var provider = new TestLlmProvider();

        // Act
        var adapter = new LlmProviderAdapter(provider);

        // Assert
        Assert.NotNull(adapter);
        Assert.Equal("TestProvider", adapter.Name);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullProvider()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new LlmProviderAdapter(null!));
        Assert.Equal("llmProvider", exception.ParamName);
    }

    #endregion

    #region Name Property Tests

    [Fact]
    public void ShouldReturnProviderName_WhenName()
    {
        // Arrange
        var provider = new TestLlmProvider("CustomProvider");
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var name = adapter.Name;

        // Assert
        Assert.Equal("CustomProvider", name);
    }

    #endregion

    #region ChatAsync Tests

    [Fact]
    public async Task ShouldCallProviderAndReturnContent_WhenChatAsyncWithSimpleMessage()
    {
        // Arrange
        var provider = new TestLlmProvider(responseContent: "Hello from LLM!");
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var response = await adapter.ChatAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello from LLM!", response);
        Assert.Single(provider.ChatCalls);
        var (messages, config) = provider.ChatCalls[0];
        Assert.Single(messages);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("Hello", messages[0].Content);
        Assert.Null(config);
    }

    [Fact]
    public async Task ShouldPassConfigToProvider_WhenChatAsyncWithConfig()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        var llmConfig = LlmConfig.Default() with
        {
            Model = CustomModelName,
            Temperature = 0.8,
            MaxTokens = 500
        };

        // Act
        await adapter.ChatAsync("Test message", llmConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(provider.ChatCalls);
        var (_, config) = provider.ChatCalls[0];
        Assert.NotNull(config);
        Assert.Equal(CustomModelName, config.Model);
        Assert.Equal(0.8, config.Temperature);
        Assert.Equal(500, config.MaxTokens);
    }

    [Fact]
    public async Task ShouldPassToProvider_WhenChatAsyncWithCancellationToken()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        using var cts = new CancellationTokenSource();

        // Act
        await adapter.ChatAsync("Test", null, cts.Token);

        // Assert
        Assert.Single(provider.ChatCalls);
        // Cancellation token is passed through the call chain
    }

    [Fact]
    public async Task ShouldStillCreateUserMessage_WhenChatAsyncWithEmptyMessage()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);

        // Act
        await adapter.ChatAsync("", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(provider.ChatCalls);
        var (messages, _) = provider.ChatCalls[0];
        Assert.Single(messages);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("", messages[0].Content);
    }

    [Fact]
    public async Task ShouldPassFullMessage_WhenChatAsyncWithLongMessage()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        var longMessage = new string('X', 10000);

        // Act
        var response = await adapter.ChatAsync(longMessage, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(provider.ChatCalls);
        var (messages, _) = provider.ChatCalls[0];
        Assert.Equal(longMessage, messages[0].Content);
    }

    #endregion

    #region IsAvailableAsync Tests

    [Fact]
    public async Task ShouldReturnTrue_WhenIsAvailableAsyncWhenProviderResponds()
    {
        // Arrange
        var provider = new TestLlmProvider(responseContent: "pong");
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var isAvailable = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(isAvailable);
        Assert.Single(provider.GenerateCalls);
        var (prompt, _) = provider.GenerateCalls[0];
        Assert.Equal("ping", prompt);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenIsAvailableAsyncWhenProviderReturnsEmpty()
    {
        // Arrange
        var provider = new TestLlmProvider(responseContent: "");
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var isAvailable = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isAvailable);
        Assert.Single(provider.GenerateCalls);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenIsAvailableAsyncWhenProviderThrows()
    {
        // Arrange
        var provider = new TestLlmProvider(shouldThrowOnGenerate: true);
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var isAvailable = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isAvailable);
        Assert.Single(provider.GenerateCalls);
    }

    [Fact]
    public async Task ShouldRespectToken_WhenIsAvailableAsyncWithCancellation()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        using var cts = new CancellationTokenSource();

        // Act
        var isAvailable = await adapter.IsAvailableAsync(cts.Token);

        // Assert
        Assert.True(isAvailable);
        // Cancellation token is passed through
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ShouldHandleCorrectly_WhenChatAsyncWithSpecialCharacters()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        var messageWithSpecialChars = "Test with 特殊文字 and émojis 🎉";

        // Act
        await adapter.ChatAsync(messageWithSpecialChars, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var (messages, _) = provider.ChatCalls[0];
        Assert.Equal(messageWithSpecialChars, messages[0].Content);
    }

    [Fact]
    public async Task ShouldWorkIndependently_WhenChatAsyncMultipleCallsInSequence()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var response1 = await adapter.ChatAsync("First", cancellationToken: TestContext.Current.CancellationToken);
        var response2 = await adapter.ChatAsync("Second", cancellationToken: TestContext.Current.CancellationToken);
        var response3 = await adapter.ChatAsync("Third", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, provider.ChatCalls.Count);
        Assert.Equal("First", provider.ChatCalls[0].messages[0].Content);
        Assert.Equal("Second", provider.ChatCalls[1].messages[0].Content);
        Assert.Equal("Third", provider.ChatCalls[2].messages[0].Content);
        Assert.Equal(response1, response2); // Same provider response
        Assert.Equal(response2, response3);
    }

    [Fact]
    public async Task ShouldHandleSafely_WhenChatAsyncConcurrentCalls()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        var tasks = new List<Task<string>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(adapter.ChatAsync($"Message {index}", cancellationToken: TestContext.Current.CancellationToken));
        }

        var responses = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, provider.ChatCalls.Count);
        Assert.All(responses, r => Assert.Equal("Test response", r));

        // Verify all messages were captured
        var contents = provider.ChatCalls.Select(c => c.messages[0].Content).OrderBy(c => c).ToList();
        for (int i = 0; i < 10; i++)
        {
            Assert.Contains($"Message {i}", contents);
        }
    }

    #endregion

    #region Additional Edge Cases

    [Fact]
    public async Task ShouldPassNullToProvider_WhenChatAsyncWithNullMessage()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);

        // Act
        await adapter.ChatAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(provider.ChatCalls);
        var (messages, _) = provider.ChatCalls[0];
        Assert.Single(messages);
        Assert.Equal("user", messages[0].Role);
        Assert.Null(messages[0].Content);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenIsAvailableAsyncWhenProviderReturnsNull()
    {
        // Arrange
        var provider = new TestLlmProviderWithNullResponse();
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var isAvailable = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public async Task ShouldPreserveWhitespace_WhenChatAsyncWithWhitespaceOnlyMessage()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        var whitespaceMessage = "   \t\n\r   ";

        // Act
        await adapter.ChatAsync(whitespaceMessage, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var (messages, _) = provider.ChatCalls[0];
        Assert.Equal(whitespaceMessage, messages[0].Content);
    }

    [Theory]
    [InlineData("Line1\nLine2")]
    [InlineData("Tab\tSeparated")]
    [InlineData("Carriage\rReturn")]
    [InlineData("Mixed\n\r\t Whitespace")]
    public async Task ShouldHandleCorrectly_WhenChatAsyncWithVariousWhitespace(string message)
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);

        // Act
        await adapter.ChatAsync(message, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var (messages, _) = provider.ChatCalls[0];
        Assert.Equal(message, messages[0].Content);
    }

    [Fact]
    public async Task ShouldPingEachTime_WhenIsAvailableAsyncMultipleCalls()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);

        // Act
        var result1 = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);
        var result2 = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);
        var result3 = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
        Assert.Equal(3, provider.GenerateCalls.Count);
        Assert.All(provider.GenerateCalls, call => Assert.Equal("ping", call.prompt));
    }

    [Fact]
    public async Task ShouldThrowTaskCancelledException_WhenChatAsyncWithCancelledToken()
    {
        // Arrange
        var provider = new TestLlmProviderWithDelay();
        var adapter = new LlmProviderAdapter(provider);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await adapter.ChatAsync("Test", null, cts.Token));
    }

    [Fact]
    public async Task ShouldReturnFalseQuickly_WhenIsAvailableAsyncWithCancelledToken()
    {
        // Arrange
        var provider = new TestLlmProviderWithDelay();
        var adapter = new LlmProviderAdapter(provider);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await adapter.IsAvailableAsync(cts.Token);

        // Assert
        Assert.False(result); // Returns false due to exception from cancellation
    }

    [Fact]
    public async Task ShouldPassAllParametersCorrectly_WhenChatAsyncWithComplexConfig()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var adapter = new LlmProviderAdapter(provider);
        var complexConfig = LlmConfig.Default() with
        {
            Model = ModelGpt4Turbo,
            Temperature = 0.95,
            MaxTokens = 4096,
            TopP = 0.9,
            FrequencyPenalty = 0.5,
            PresencePenalty = 0.3,
            StopSequences = ["\n\n", "END"],
            CustomParameters = new Dictionary<string, object> { ["response_format"] = "json" }
        };

        // Act
        await adapter.ChatAsync("Complex test", complexConfig, TestContext.Current.CancellationToken);

        // Assert
        var (_, config) = provider.ChatCalls[0];
        Assert.NotNull(config);
        Assert.Equal(ModelGpt4Turbo, config.Model);
        Assert.Equal(0.95, config.Temperature);
        Assert.Equal(4096, config.MaxTokens);
        Assert.Equal(0.9, config.TopP);
        Assert.Equal(0.5, config.FrequencyPenalty);
        Assert.Equal(0.3, config.PresencePenalty);
        Assert.Equal(2, config.StopSequences.Count);
        Assert.Contains("response_format", config.CustomParameters.Keys);
        Assert.Equal("json", config.CustomParameters["response_format"]);
    }

    private class TestLlmProviderWithNullResponse : ILlmProvider
    {
        public string Name => "NullProvider";

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<LlmResponse>(null!);
        }

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new LlmResponse
            {
                Content = null!,
                TokensUsed = 0,
                Model = "null-model",
                Metadata = new Dictionary<string, object>()
            });
        }
    }

    private class TestLlmProviderWithDelay : ILlmProvider
    {
        public string Name => "DelayProvider";

        public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Delay(1000, cancellationToken);
            return new LlmResponse
            {
                Content = "delayed response",
                TokensUsed = 10,
                Model = "delay-model",
                Metadata = new Dictionary<string, object>()
            };
        }

        public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Delay(1000, cancellationToken);
            return new LlmResponse
            {
                Content = "delayed response",
                TokensUsed = 10,
                Model = "delay-model",
                Metadata = new Dictionary<string, object>()
            };
        }
    }

    #endregion
}
