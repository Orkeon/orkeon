using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmResponseTests
{
    private static readonly int[] Int12345 = [1, 2, 3, 4, 5];
    private static readonly string[] WeatherChunks = ["The ", "weather ", "today ", "is ", "sunny."];
    #region LlmResponse Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingLlmResponseWithDefaultConstructor()
    {
        // Act
        var response = new LlmResponse();

        // Assert
        Assert.Equal(string.Empty, response.Content);
        Assert.Equal(0, response.TokensUsed);
        Assert.Null(response.PromptTokens);
        Assert.Null(response.CompletionTokens);
        Assert.Null(response.Model);
        Assert.NotNull(response.Metadata);
    }

    [Fact]
    public void ShouldSetValues_WhenUsingLlmResponseWithRecordExpression()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["model"] = ModelGpt4,
            ["tokens_used"] = 100
        };

        // Act
        var response = new LlmResponse() with
        {
            Content = "This is a test response",
            TokensUsed = 150,
            PromptTokens = 50,
            CompletionTokens = 100,
            Model = ModelGpt4Turbo,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("This is a test response", response.Content);
        Assert.Equal(150, response.TokensUsed);
        Assert.Equal(50, response.PromptTokens);
        Assert.Equal(100, response.CompletionTokens);
        Assert.Equal(ModelGpt4Turbo, response.Model);
        Assert.Equal(metadata, response.Metadata);
    }

    [Fact]
    public void ShouldSetProperties_WhenUsingLlmResponseUsingInitializerSyntax()
    {
        // Act
        var response = new LlmResponse
        {
            Content = "Generated content",
            TokensUsed = 200,
            PromptTokens = 75,
            CompletionTokens = 125,
            Model = ModelClaude3,
            Metadata = new Dictionary<string, object>
            {
                ["temperature"] = 0.7
            }
        };

        // Assert
        Assert.Equal("Generated content", response.Content);
        Assert.Equal(200, response.TokensUsed);
        Assert.Equal(75, response.PromptTokens);
        Assert.Equal(125, response.CompletionTokens);
        Assert.Equal(ModelClaude3, response.Model);
        Assert.Equal(0.7, response.Metadata["temperature"]);
    }

    [Fact]
    public void ShouldBeConsistent_WhenUsingLlmResponseTokenizingCalculations()
    {
        // Arrange & Act — use 'with' to derive a new record with computed TokensUsed
        var initial = new LlmResponse
        {
            PromptTokens = 100,
            CompletionTokens = 200
        };
        var response = initial with
        {
            TokensUsed = initial.PromptTokens!.Value + initial.CompletionTokens!.Value
        };

        // Assert
        Assert.Equal(300, response.TokensUsed);
        Assert.Equal(response.PromptTokens + response.CompletionTokens, response.TokensUsed);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingLlmResponseWithEmptyContent()
    {
        // Act
        var response = new LlmResponse
        {
            Content = "",
            TokensUsed = 0
        };

        // Assert
        Assert.Empty(response.Content);
        Assert.Equal(0, response.TokensUsed);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingLlmResponseUsingVeryLongContent()
    {
        // Arrange
        var longContent = string.Join(" ", Enumerable.Repeat("This is a test.", 1000));

        // Act
        var response = new LlmResponse
        {
            Content = longContent,
            TokensUsed = 5000
        };

        // Assert
        Assert.Equal(longContent, response.Content);
        Assert.True(response.Content.Length > 10000);
    }

    #endregion

    #region LlmMessage Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingLlmMessageWithDefaultConstructor()
    {
        // Act
        var message = new LlmMessage();

        // Assert
        Assert.Equal(string.Empty, message.Role);
        Assert.Equal(string.Empty, message.Content);
        Assert.Null(message.Name);
        Assert.Null(message.FunctionCallInfo);
    }

    [Fact]
    public void ShouldSetValues_WhenUsingLlmMessageWithRecordExpression()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            { "location", "London" },
            { "unit", "celsius" }
        };

        // Act — use 'with' to derive a new record
        var message = new LlmMessage() with
        {
            Role = "assistant",
            Content = "I'll check the weather for you",
            Name = "weather_bot",
            FunctionCallInfo = new FunctionCallInfo("get_weather", arguments)
        };

        // Assert
        Assert.Equal("assistant", message.Role);
        Assert.Equal("I'll check the weather for you", message.Content);
        Assert.Equal("weather_bot", message.Name);
        Assert.NotNull(message.FunctionCallInfo);
        Assert.Equal("get_weather", message.FunctionCallInfo.Name);
        Assert.Equal("London", message.FunctionCallInfo.GetArgument<string>("location"));
        Assert.Equal("celsius", message.FunctionCallInfo.GetArgument<string>("unit"));
    }

    [Fact]
    public void ShouldSetProperties_WhenUsingLlmMessageUsingInitializerSyntax()
    {
        // Act
        var message = new LlmMessage
        {
            Role = "user",
            Content = "What's the weather like?",
            Name = "john_doe"
        };

        // Assert
        Assert.Equal("user", message.Role);
        Assert.Equal("What's the weather like?", message.Content);
        Assert.Equal("john_doe", message.Name);
        Assert.Null(message.FunctionCallInfo);
    }

    [Fact]
    public void ShouldBeSupported_WhenUsingLlmMessageUsingStandardRoles()
    {
        // Test standard OpenAI roles
        var systemMessage = new LlmMessage { Role = "system", Content = "You are a helpful assistant." };
        var userMessage = new LlmMessage { Role = "user", Content = "Hello!" };
        var assistantMessage = new LlmMessage { Role = "assistant", Content = "Hi there!" };
        var functionMessage = new LlmMessage { Role = "function", Name = "get_weather", Content = "{\"temp\": 20}" };

        // Assert
        Assert.Equal("system", systemMessage.Role);
        Assert.Equal("user", userMessage.Role);
        Assert.Equal("assistant", assistantMessage.Role);
        Assert.Equal("function", functionMessage.Role);
        Assert.Equal("get_weather", functionMessage.Name);
    }

    [Fact]
    public void ShouldWithComplexData_WhenUsingLlmMessageUsingFunctionCallInfo()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            { "data", Int12345 },
            { "operation", "sum" },
            { "options", new { parallel = true, timeout = 30 } }
        };

        // Act
        var message = new LlmMessage
        {
            Role = "assistant",
            Content = "",
            FunctionCallInfo = new FunctionCallInfo("process_data", arguments)
        };

        // Assert
        Assert.NotNull(message.FunctionCallInfo);
        Assert.Equal("process_data", message.FunctionCallInfo.Name);
        Assert.Equal(3, message.FunctionCallInfo.Arguments.Count);
    }

    #endregion

    #region Scenario Tests

    [Fact]
    public void ShouldSuccessfulApiResponse_WhenUsingScenario()
    {
        // Simulate a successful API response
        var response = new LlmResponse
        {
            Content = "The capital of France is Paris. It's known for the Eiffel Tower.",
            TokensUsed = 150,
            PromptTokens = 25,
            CompletionTokens = 125,
            Model = ModelGpt35Turbo,
            Metadata = new Dictionary<string, object>
            {
                ["response_time_ms"] = 800,
                ["temperature"] = 0.7,
                ["model"] = ModelGpt35Turbo
            }
        };

        // Assert
        Assert.NotEmpty(response.Content);
        Assert.Equal(response.PromptTokens + response.CompletionTokens, response.TokensUsed);
        Assert.Equal(ModelGpt35Turbo, response.Model);
        Assert.Equal(response.Model, response.Metadata["model"]);
    }

    [Fact]
    public void ShouldStreamingResponse_WhenUsingScenario()
    {
        // Simulate building a response from streaming chunks
        var chunks = WeatherChunks;

        // Build content from chunks, then create immutable response
        var content = string.Concat(chunks);
        var response = new LlmResponse
        {
            Content = content,
            PromptTokens = 10,
            CompletionTokens = 5,
            TokensUsed = 15,
            Model = "gpt-4-streaming"
        };

        // Assert
        Assert.Equal("The weather today is sunny.", response.Content);
        Assert.Equal(15, response.TokensUsed);
    }

    [Fact]
    public void ShouldErrorResponse_WhenUsingScenario()
    {
        // Simulate an error response
        var response = new LlmResponse
        {
            Content = "",
            TokensUsed = 0,
            Model = ModelGpt4,
            Metadata = new Dictionary<string, object>
            {
                ["error"] = "Rate limit exceeded. Please try again in 60 seconds.",
                ["response_time_ms"] = 50
            }
        };

        // Assert
        Assert.Empty(response.Content);
        Assert.Equal(0, response.TokensUsed);
        Assert.True(response.Metadata.ContainsKey("error"));
        Assert.Contains("Rate limit", (string)response.Metadata["error"]);
    }

    [Fact]
    public void ShouldConversationHistory_WhenUsingScenario()
    {
        // Simulate a conversation with multiple messages
        var messages = new List<LlmMessage>
        {
            new LlmMessage
            {
                Role = "system",
                Content = "You are a helpful coding assistant."
            },
            new LlmMessage
            {
                Role = "user",
                Content = "How do I create a list in Python?"
            },
            new LlmMessage
            {
                Role = "assistant",
                Content = "In Python, you can create a list using square brackets: my_list = [1, 2, 3]"
            },
            new LlmMessage
            {
                Role = "user",
                Content = "How do I add items to it?"
            },
            new LlmMessage
            {
                Role = "assistant",
                Content = "You can use append() to add single items: my_list.append(4)"
            }
        };

        // Assert conversation structure
        Assert.Equal(5, messages.Count);
        Assert.Single(messages, m => m.Role == "system");
        Assert.Equal(2, messages.Count(m => m.Role == "user"));
        Assert.Equal(2, messages.Count(m => m.Role == "assistant"));
        Assert.All(messages, m => Assert.NotEmpty(m.Content));
    }

    [Fact]
    public void ShouldFunctionCallingFlow_WhenUsingScenario()
    {
        // Simulate a function calling scenario
        var messages = new List<LlmMessage>
        {
            // User asks for weather
            new LlmMessage
            {
                Role = "user",
                Content = "What's the weather in London?"
            },

            // Assistant decides to call a function
            new LlmMessage
            {
                Role = "assistant",
                Content = "",
                FunctionCallInfo = new FunctionCallInfo("get_weather", new Dictionary<string, object>
            {
                { "location", "London" },
                { "unit", "celsius" }
            })
            },

            // Function returns result
            new LlmMessage
            {
                Role = "function",
                Name = "get_weather",
                Content = "{\"temperature\": 18, \"condition\": \"partly cloudy\"}"
            },

            // Assistant provides final response
            new LlmMessage
            {
                Role = "assistant",
                Content = "The weather in London is 18 degrees C and partly cloudy."
            }
        };

        // Create response for the final message
        var response = new LlmResponse
        {
            Content = messages.Last().Content,
            TokensUsed = 250,
            PromptTokens = 200,
            CompletionTokens = 50,
            Model = "gpt-4-functions"
        };

        // Assert
        Assert.Equal(4, messages.Count);
        Assert.NotNull(messages[1].FunctionCallInfo);
        Assert.Equal("function", messages[2].Role);
        Assert.Contains("18", response.Content);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldNotThrow_WhenUsingEdgeCaseWithNullContent()
    {
        // Act — use initializer to force null via init-only properties
        var response = new LlmResponse { Content = null! };
        var message = new LlmMessage { Content = null! };

        // Assert - Should not throw
        Assert.Null(response.Content);
        Assert.Null(message.Content);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseWithNegativeTokens()
    {
        // Records don't prevent negative values
        var response = new LlmResponse
        {
            TokensUsed = -100,
            PromptTokens = -50,
            CompletionTokens = -50
        };

        // Assert
        Assert.Equal(-100, response.TokensUsed);
        Assert.Equal(-50, response.PromptTokens);
        Assert.Equal(-50, response.CompletionTokens);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseWithEmptyRole()
    {
        // Act
        var message = new LlmMessage
        {
            Role = "",
            Content = "Content without role"
        };

        // Assert
        Assert.Empty(message.Role);
        Assert.NotEmpty(message.Content);
    }

    [Fact]
    public void ShouldVeryLongModelName_WhenUsingEdgeCase()
    {
        // Arrange
        var longModelName = "gpt-4-turbo-preview-2024-01-25-with-extended-context-window-and-enhanced-capabilities-version-1.0.0";

        // Act
        var response = new LlmResponse
        {
            Model = longModelName
        };

        // Assert
        Assert.Equal(longModelName, response.Model);
    }

    [Fact]
    public void ShouldNullFunctionCallInfo_WhenUsingEdgeCase()
    {
        // Act
        var message = new LlmMessage
        {
            Role = "assistant",
            FunctionCallInfo = null
        };

        // Assert
        Assert.Null(message.FunctionCallInfo);
    }

    [Fact]
    public void ShouldMismatchedTokenCounts_WhenUsingEdgeCase()
    {
        // This demonstrates that the class doesn't enforce consistency
        var response = new LlmResponse
        {
            TokensUsed = 100,
            PromptTokens = 60,
            CompletionTokens = 80 // Sum is 140, not 100
        };

        // Assert - No validation, so mismatched values are allowed
        Assert.Equal(100, response.TokensUsed);
        Assert.Equal(140, response.PromptTokens + response.CompletionTokens);
        Assert.NotEqual(response.TokensUsed, response.PromptTokens + response.CompletionTokens);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldResponseWithDetailedMetadata_WhenUsingComplexScenario()
    {
        // Create a response with comprehensive metadata
        var response = new LlmResponse
        {
            Content = "This is a detailed response with multiple paragraphs.\n\nIt includes various information.",
            TokensUsed = 500,
            PromptTokens = 150,
            CompletionTokens = 350,
            Model = ModelGpt4,
            Metadata = new Dictionary<string, object>
            {
                ["tokens_used"] = 500,
                ["tokens_limit"] = 4000,
                ["response_time"] = TimeSpan.FromSeconds(2.5),
                ["model"] = ModelGpt4,
                ["temperature"] = 0.8,
                ["request_id"] = "req_abc123",
                ["finish_reason"] = "stop",
                ["system_fingerprint"] = "fp_123456"
            }
        };

        // Assert consistency between response and metadata
        Assert.Equal(response.TokensUsed, (int)response.Metadata["tokens_used"]);
        Assert.Equal(response.Model, (string)response.Metadata["model"]);
        Assert.True(response.TokensUsed < (int)response.Metadata["tokens_limit"]);
        Assert.Equal("stop", response.Metadata["finish_reason"]);
    }

    [Fact]
    public void ShouldMultiTurnConversationWithContext_WhenUsingComplexScenario()
    {
        // Simulate a multi-turn conversation with context management
        var conversation = new List<(LlmMessage Message, LlmResponse? Response)>
        {
            // System prompt
            (new LlmMessage
            {
                Role = "system",
                Content = "You are an expert Python programmer."
            }, null),

            // Turn 1
            (new LlmMessage
            {
                Role = "user",
                Content = "How do I read a CSV file?"
            }, null),
            (new LlmMessage
            {
                Role = "assistant",
                Content = "You can use pandas: df = pd.read_csv('file.csv')"
            }, new LlmResponse
            {
                Content = "You can use pandas: df = pd.read_csv('file.csv')",
                TokensUsed = 50,
                PromptTokens = 30,
                CompletionTokens = 20
            }),

            // Turn 2
            (new LlmMessage
            {
                Role = "user",
                Content = "What if I don't want to use pandas?"
            }, null),
            (new LlmMessage
            {
                Role = "assistant",
                Content = "You can use the built-in csv module:\nimport csv\nwith open('file.csv', 'r') as f:\n    reader = csv.reader(f)"
            }, new LlmResponse
            {
                Content = "You can use the built-in csv module:\nimport csv\nwith open('file.csv', 'r') as f:\n    reader = csv.reader(f)",
                TokensUsed = 120,
                PromptTokens = 70,
                CompletionTokens = 50
            })
        };

        // Calculate total tokens across conversation
        var totalTokens = conversation
            .Where(c => c.Response != null)
            .Sum(c => c.Response!.TokensUsed);

        // Assert
        Assert.Equal(5, conversation.Count);
        Assert.Equal(170, totalTokens);
        Assert.All(conversation.Where(c => c.Response != null),
            c => Assert.Equal(c.Message.Content, c.Response!.Content));
    }

    #endregion
}
