using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Domain.Tests.Interfaces;

public class ILlmProviderTests
{
    // Test implementation of ILlmProvider
    private class TestLlmProvider : ILlmProvider
    {
        private readonly Func<string, LlmConfig?, CancellationToken, Task<LlmResponse>>? _generateFunc;
        private readonly Func<LlmMessage[], LlmConfig?, CancellationToken, Task<LlmResponse>>? _chatFunc;

        public string Name { get; set; }
        public int GenerateAsyncCount { get; private set; }
        public int ChatAsyncCount { get; private set; }
        public string? LastGeneratePrompt { get; private set; }
        public LlmMessage[]? LastChatMessages { get; private set; }
        public LlmConfig? LastConfig { get; private set; }
        public bool ShouldThrowOnGenerate { get; set; }
        public bool ShouldThrowOnChat { get; set; }

        public TestLlmProvider(
            string name = "TestProvider",
            Func<string, LlmConfig?, CancellationToken, Task<LlmResponse>>? generateFunc = null,
            Func<LlmMessage[], LlmConfig?, CancellationToken, Task<LlmResponse>>? chatFunc = null)
        {
            Name = name;
            _generateFunc = generateFunc;
            _chatFunc = chatFunc;
        }

        public async System.Threading.Tasks.Task<LlmResponse> GenerateAsync(
            string prompt,
            LlmConfig? config = null,
            CancellationToken cancellationToken = default)
        {
            GenerateAsyncCount++;
            LastGeneratePrompt = prompt;
            LastConfig = config;

            if (ShouldThrowOnGenerate)
                throw new InvalidOperationException("Generate operation failed");

            cancellationToken.ThrowIfCancellationRequested();

            if (_generateFunc != null)
                return await _generateFunc(prompt, config, cancellationToken);

            await System.Threading.Tasks.Task.Delay(10, cancellationToken);

            return CreateDefaultResponse($"Generated response for: {prompt}", config);
        }

        public async System.Threading.Tasks.Task<LlmResponse> ChatAsync(
            LlmMessage[] messages,
            LlmConfig? config = null,
            CancellationToken cancellationToken = default)
        {
            ChatAsyncCount++;
            LastChatMessages = messages;
            LastConfig = config;

            if (ShouldThrowOnChat)
                throw new InvalidOperationException("Chat operation failed");

            cancellationToken.ThrowIfCancellationRequested();

            if (_chatFunc != null)
                return await _chatFunc(messages, config, cancellationToken);

            await System.Threading.Tasks.Task.Delay(10, cancellationToken);

            var lastMessage = messages.LastOrDefault()?.Content ?? "No messages";
            return CreateDefaultResponse($"Chat response to: {lastMessage}", config);
        }

        private static LlmResponse CreateDefaultResponse(string content, LlmConfig? config)
        {
            return new LlmResponse
            {
                Content = content,
                TokensUsed = 100,
                PromptTokens = 50,
                CompletionTokens = 50,
                Model = config?.Model ?? TestModelName,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = config?.Model ?? TestModelName,
                    ["temperature"] = config?.Temperature ?? 0.7,
                    ["tokens_used"] = 100,
                    ["response_time"] = TimeSpan.FromMilliseconds(250)
                }
            };
        }
    }

    [Fact]
    public void ShouldHaveProviderName_WhenUsingILlmProvider()
    {
        // Arrange & Act
        var provider = new TestLlmProvider("OpenAI");

        // Assert
        Assert.Equal("OpenAI", provider.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResponse_WhenGeneratingAsyncWithSimplePrompt()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var prompt = "What is the capital of France?";

        // Act
        var response = await provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("What is the capital of France?", response.Content);
        Assert.Equal(100, response.TokensUsed);
        Assert.Equal(1, provider.GenerateAsyncCount);
        Assert.Equal(prompt, provider.LastGeneratePrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseConfiguration_WhenGeneratingAsyncWithConfig()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var config = LlmConfig.Default() with
        {
            Model = ModelGpt4,
            Temperature = 0.9,
            MaxTokens = 2000,
            TopP = 0.95
        };
        var prompt = "Generate a story";

        // Act
        var response = await provider.GenerateAsync(prompt, config, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ModelGpt4, response.Model);
        Assert.Equal(ModelGpt4, (string)response.Metadata["model"]);
        Assert.Equal(0.9, (double)response.Metadata["temperature"]);
        Assert.Equal(config, provider.LastConfig);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenGeneratingAsyncWithCustomFunction()
    {
        // Arrange
        var customResponse = new LlmResponse
        {
            Content = "Paris is the capital of France",
            TokensUsed = 15,
            PromptTokens = 8,
            CompletionTokens = 7,
            Model = CustomModelName
        };

        var provider = new TestLlmProvider(
            generateFunc: (prompt, config, ct) => System.Threading.Tasks.Task.FromResult(customResponse));

        // Act
        var response = await provider.GenerateAsync("What is the capital of France?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(customResponse, response);
        Assert.Equal("Paris is the capital of France", response.Content);
        Assert.Equal(15, response.TokensUsed);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenGeneratingAsyncWithCancellation()
    {
        // Arrange
        var provider = new TestLlmProvider(
            generateFunc: async (prompt, config, ct) =>
            {
                await System.Threading.Tasks.Task.Delay(100, ct);
                return new LlmResponse { Content = "Response" };
            });

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GenerateAsync("Prompt", null, cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResponse_WhenUsingChatAsyncWithSingleMessage()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var messages = new[]
        {
            new LlmMessage { Role = "user", Content = "Hello, how are you?" }
        };

        // Act
        var response = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("Hello, how are you?", response.Content);
        Assert.Equal(1, provider.ChatAsyncCount);
        Assert.Equal(messages, provider.LastChatMessages);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleMultipleMessages_WhenUsingChatAsyncWithConversation()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "You are a helpful assistant." },
            new LlmMessage { Role = "user", Content = "What's the weather like?" },
            new LlmMessage { Role = "assistant", Content = "I don't have access to weather data." },
            new LlmMessage { Role = "user", Content = "Can you help with math instead?" }
        };

        // Act
        var response = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("Can you help with math instead?", response.Content);
        Assert.Equal(4, provider.LastChatMessages!.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldIncludeFunctionCallData_WhenUsingChatAsyncWithFunctionCall()
    {
        // Arrange
        var messages = new[]
        {
            new LlmMessage
            {
                Role = "assistant",
                Content = "I'll check the weather for you.",
                FunctionCallInfo = new FunctionCallInfo("get_weather", new Dictionary<string, object>
                {
                    ["location"] = "Paris",
                    ["unit"] = "celsius"
                })
            }
        };

        var provider = new TestLlmProvider(
            chatFunc: (msgs, config, ct) =>
            {
                var msg = msgs[0];
                Assert.NotNull(msg.FunctionCallInfo);
                Assert.Equal("get_weather", msg.FunctionCallInfo.Name);

                return System.Threading.Tasks.Task.FromResult(new LlmResponse
                {
                    Content = "Weather function called"
                });
            });

        // Act
        var response = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Weather function called", response.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldApplyConfiguration_WhenUsingChatAsyncWithConfig()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var messages = new[]
        {
            new LlmMessage { Role = "user", Content = "Test message" }
        };
        var config = LlmConfig.Gpt35Turbo(TestApiKey) with
        {
            Temperature = 0.5,
            MaxTokens = 1000
        };

        // Act
        var response = await provider.ChatAsync(messages, config, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ModelGpt35Turbo, response.Model);
        Assert.Equal(0.5, (double)response.Metadata["temperature"]);
        Assert.Equal(config, provider.LastConfig);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenUsingChatAsyncWithCustomFunction()
    {
        // Arrange
        var customResponse = new LlmResponse
        {
            Content = "I'm doing well, thank you!",
            TokensUsed = 20,
            Model = "chat-model",
            Metadata = new Dictionary<string, object>
            {
                ["model"] = "chat-model",
                ["response_time"] = TimeSpan.FromMilliseconds(150)
            }
        };

        var provider = new TestLlmProvider(
            chatFunc: (messages, config, ct) => System.Threading.Tasks.Task.FromResult(customResponse));

        var messages = new[]
        {
            new LlmMessage { Role = "user", Content = "How are you?" }
        };

        // Act
        var response = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(customResponse, response);
        Assert.Equal("I'm doing well, thank you!", response.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingILlmProviderWithDifferentConfigs()
    {
        // Arrange
        var provider = new TestLlmProvider();
        var gpt4Config = LlmConfig.WithDefaultModel("api-key-1");
        var gpt35Config = LlmConfig.Gpt35Turbo("api-key-2");
        var claudeConfig = LlmConfig.Claude("api-key-3");
        var ollamaConfig = LlmConfig.Ollama(ModelMistral);

        // Act
        var response1 = await provider.GenerateAsync("Test 1", gpt4Config, TestContext.Current.CancellationToken);
        var response2 = await provider.GenerateAsync("Test 2", gpt35Config, TestContext.Current.CancellationToken);
        var response3 = await provider.GenerateAsync("Test 3", claudeConfig, TestContext.Current.CancellationToken);
        var response4 = await provider.GenerateAsync("Test 4", ollamaConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ModelDefault, response1.Model);
        Assert.Equal(ModelGpt35Turbo, response2.Model);
        Assert.Equal(ModelClaude3Opus, response3.Model);
        Assert.Equal(ModelMistral, response4.Model);
        Assert.Equal(4, provider.GenerateAsyncCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowExceptions_WhenUsingILlmProviderWithErrorHandling()
    {
        // Arrange
        var provider = new TestLlmProvider
        {
            ShouldThrowOnGenerate = true,
            ShouldThrowOnChat = true
        };

        // Act & Assert - Generate should throw
        var generateException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GenerateAsync("test", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Generate operation failed", generateException.Message);

        // Act & Assert - Chat should throw
        var chatException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ChatAsync([new LlmMessage { Role = "user", Content = "test" }], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Chat operation failed", chatException.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleConversation_WhenUsingILlmProviderWithCompleteWorkflow()
    {
        // Arrange
        var conversationLog = new List<string>();

        var provider = new TestLlmProvider(
            chatFunc: async (messages, config, ct) =>
            {
                await System.Threading.Tasks.Task.Delay(10, ct);
                var lastMessage = messages.Last();
                conversationLog.Add($"{lastMessage.Role}: {lastMessage.Content}");

                var responseContent = lastMessage.Content switch
                {
                    var c when c.Contains("weather", StringComparison.OrdinalIgnoreCase) => "The weather is sunny and warm.",
                    var c when c.Contains("temperature", StringComparison.OrdinalIgnoreCase) => "It's about 25 degrees C (77 degrees F).",
                    var c when c.Contains("thank", StringComparison.OrdinalIgnoreCase) => "You're welcome! Have a great day!",
                    _ => "I can help you with that."
                };

                return new LlmResponse
                {
                    Content = responseContent,
                    TokensUsed = messages.Length * 20,
                    PromptTokens = messages.Length * 10,
                    CompletionTokens = 10,
                    Model = config?.Model ?? "default-model"
                };
            });

        var conversation = new List<LlmMessage>();
        var config = LlmConfig.Default();

        // Act - Simulate a conversation
        conversation.Add(new LlmMessage { Role = "system", Content = "You are a helpful weather assistant." });
        conversation.Add(new LlmMessage { Role = "user", Content = "What's the weather like today?" });

        var response1 = await provider.ChatAsync(conversation.ToArray(), config, TestContext.Current.CancellationToken);
        conversation.Add(new LlmMessage { Role = "assistant", Content = response1.Content });

        conversation.Add(new LlmMessage { Role = "user", Content = "What about the temperature?" });
        var response2 = await provider.ChatAsync(conversation.ToArray(), config, TestContext.Current.CancellationToken);
        conversation.Add(new LlmMessage { Role = "assistant", Content = response2.Content });

        conversation.Add(new LlmMessage { Role = "user", Content = "Thank you!" });
        var response3 = await provider.ChatAsync(conversation.ToArray(), config, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The weather is sunny and warm.", response1.Content);
        Assert.Contains("25", response2.Content);
        Assert.Equal("You're welcome! Have a great day!", response3.Content);

        Assert.Equal(3, provider.ChatAsyncCount);
        Assert.Equal(3, conversationLog.Count);
        Assert.Contains("user: What's the weather like today?", conversationLog);
        Assert.Contains("user: What about the temperature?", conversationLog);
        Assert.Contains("user: Thank you!", conversationLog);

        // Token usage should increase with conversation length
        Assert.Equal(40, response1.TokensUsed); // 2 messages
        Assert.Equal(80, response2.TokensUsed); // 4 messages
        Assert.Equal(120, response3.TokensUsed); // 6 messages
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldTrackResponseMetrics_WhenUsingILlmProviderWithMetadata()
    {
        // Arrange
        var provider = new TestLlmProvider(
            generateFunc: async (prompt, config, ct) =>
            {
                var startTime = DateTime.UtcNow;
                await System.Threading.Tasks.Task.Delay(100, ct);
                var responseTime = DateTime.UtcNow - startTime;

                return new LlmResponse
                {
                    Content = "Response with detailed metadata",
                    TokensUsed = 250,
                    PromptTokens = 100,
                    CompletionTokens = 150,
                    Model = ModelGpt4,
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = ModelGpt4,
                        ["temperature"] = 0.8,
                        ["tokens_used"] = 250,
                        ["tokens_limit"] = 4000,
                        ["response_time"] = responseTime,
                        ["provider"] = "OpenAI",
                        ["version"] = "2024-01",
                        ["region"] = "us-east-1"
                    }
                };
            });

        // Act
        var response = await provider.GenerateAsync("Generate with metadata", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Metadata);
        Assert.Equal(250, (int)response.Metadata["tokens_used"]);
        Assert.Equal(4000, (int)response.Metadata["tokens_limit"]);
        Assert.NotNull(response.Metadata["response_time"]);
        Assert.True(((TimeSpan)response.Metadata["response_time"]).TotalMilliseconds >= 90);
        Assert.Equal("OpenAI", (string)response.Metadata["provider"]);
        Assert.Equal("2024-01", (string)response.Metadata["version"]);
        Assert.Equal("us-east-1", (string)response.Metadata["region"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOperateIndependently_WhenUsingILlmProviderWithMultipleProviders()
    {
        // Arrange
        var openAiProvider = new TestLlmProvider("OpenAI",
            generateFunc: (prompt, config, ct) => System.Threading.Tasks.Task.FromResult(
                new LlmResponse { Content = $"OpenAI: {prompt}", Model = ModelGpt4 }));

        var claudeProvider = new TestLlmProvider("Claude",
            generateFunc: (prompt, config, ct) => System.Threading.Tasks.Task.FromResult(
                new LlmResponse { Content = $"Claude: {prompt}", Model = ModelClaude3 }));

        var ollamaProvider = new TestLlmProvider("Ollama",
            generateFunc: (prompt, config, ct) => System.Threading.Tasks.Task.FromResult(
                new LlmResponse { Content = $"Ollama: {prompt}", Model = ModelLlama2 }));

        // Act
        var tasks = new[]
        {
            openAiProvider.GenerateAsync("Hello", cancellationToken: TestContext.Current.CancellationToken),
            claudeProvider.GenerateAsync("Hello", cancellationToken: TestContext.Current.CancellationToken),
            ollamaProvider.GenerateAsync("Hello", cancellationToken: TestContext.Current.CancellationToken)
        };

        var responses = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(3, responses.Length);
        Assert.Equal("OpenAI: Hello", responses[0].Content);
        Assert.Equal("Claude: Hello", responses[1].Content);
        Assert.Equal("Ollama: Hello", responses[2].Content);

        Assert.Equal(1, openAiProvider.GenerateAsyncCount);
        Assert.Equal(1, claudeProvider.GenerateAsyncCount);
        Assert.Equal(1, ollamaProvider.GenerateAsyncCount);
    }

    [Fact]
    public void ShouldCreateCorrectConfigs_WhenUsingLlmConfigUsingStaticFactories()
    {
        // Arrange & Act
        var defaultConfig = LlmConfig.Default();
        var gpt4Config = LlmConfig.WithDefaultModel("key1");
        var gpt35Config = LlmConfig.Gpt35Turbo("key2");
        var claudeConfig = LlmConfig.Claude("key3");
        var ollamaConfig = LlmConfig.Ollama(ModelMistral);

        // Assert
        Assert.Equal(ModelDefault, defaultConfig.Model);
        Assert.Null(defaultConfig.ApiKey);

        Assert.Equal(ModelDefault, gpt4Config.Model);
        Assert.Equal("key1", gpt4Config.ApiKey);

        Assert.Equal(ModelGpt35Turbo, gpt35Config.Model);
        Assert.Equal("key2", gpt35Config.ApiKey);

        Assert.Equal(ModelClaude3Opus, claudeConfig.Model);
        Assert.Equal("key3", claudeConfig.ApiKey);
        Assert.Equal(new Uri("https://api.anthropic.com"), claudeConfig.BaseUrl);

        Assert.Equal(ModelMistral, ollamaConfig.Model);
        Assert.Equal(new Uri(EndpointOllamaDefault), ollamaConfig.BaseUrl);
    }
}

#pragma warning restore CS0618
