using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// The adapter must honor the standard <see cref="ChatOptions.ResponseFormat"/> property —
/// not only the <see cref="LlmChatOptionsKeys.ResponseFormat"/> AdditionalProperties key
/// stashed by the orchestrator. Callers built on plain Microsoft.Extensions.AI options
/// (RAG retrieval evaluator, groundedness checker, query complexity classifier) set
/// <c>ChatResponseFormat.Json</c> and expect providers wiring <c>response_format</c>
/// (e.g. DeepSeek <c>json_object</c>) to receive it.
/// </summary>
public class AdapterResponseFormatTests
{
    [Fact]
    public async Task StandardChatOptionsResponseFormatJson_ReachesTheProvider()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.NotNull(capturingProvider.LastConfig);
        Assert.NotNull(capturingProvider.LastConfig!.ResponseFormat);
        Assert.Equal("json_object", capturingProvider.LastConfig.ResponseFormat!.Type);
    }

    [Fact]
    public async Task AdditionalPropertiesKey_KeepsPriority_OverStandardResponseFormat()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [LlmChatOptionsKeys.ResponseFormat] = LlmResponseFormat.Text(),
            }
        };

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.Equal("text", capturingProvider.LastConfig!.ResponseFormat!.Type);
    }

    [Fact]
    public async Task NoFormatInOptions_PreservesTheBaseConfigFormat()
    {
        var capturingProvider = new CapturingLlmProvider();
        var baseConfig = LlmConfig.Create("deepseek-v4-flash") with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        };
        using var adapter = new LlmProviderToChatClientAdapter(capturingProvider, baseConfig);

        // Temperature forces the overrides path without carrying any response format.
        var options = new ChatOptions { Temperature = 0.1f };
        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.Equal("json_object", capturingProvider.LastConfig!.ResponseFormat!.Type);
    }

    // ── structured_output: grammar or json_schema, by capability (GAP-14) ──

    private const string DeliverableSchema = """{"type":"object","properties":{"name":{"type":"string"}}}""";
    private const string DeliverableGrammar = "root ::= \"{}\"";

    private static ChatOptions StructuredOutputOptions() => new()
    {
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [LlmChatOptionsKeys.GrammarGbnf] = DeliverableGrammar,
            [LlmChatOptionsKeys.StructuredOutput] = LlmResponseFormat.JsonSchema("structured_output", DeliverableSchema, strict: false),
        },
    };

    /// <summary>
    /// A cloud provider that takes a JSON Schema gets the deliverable's schema as
    /// <c>response_format: json_schema</c> instead of a grammar it cannot honour.
    /// </summary>
    [Fact]
    public async Task StructuredOutput_TravelsAsJsonSchema_OnAProviderThatDeclaresIt()
    {
        var provider = new CapturingLlmProvider
        {
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonSchema },
        };
        using var adapter = new LlmProviderToChatClientAdapter(provider, LlmConfig.Create("gpt-test"));

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, StructuredOutputOptions(), CancellationToken.None);

        var format = provider.LastConfig!.ResponseFormat!;
        Assert.Equal("json_schema", format.Type);
        Assert.Equal(DeliverableSchema, format.Schema!.Schema);
        Assert.Null(provider.LastConfig.GrammarGbnf);
    }

    /// <summary>
    /// <c>llama-server</c> refuses a request carrying both a grammar and a JSON Schema: an
    /// endpoint configured for the grammar gets the grammar alone.
    /// </summary>
    [Fact]
    public async Task StructuredOutput_TravelsAsGrammar_WhenTheEndpointTakesOne()
    {
        var provider = new CapturingLlmProvider
        {
            Capabilities = new LlmProviderCapabilities
            {
                ResponseFormat = ResponseFormatSupport.JsonSchema,
                GbnfGrammar = true,
            },
        };
        using var adapter = new LlmProviderToChatClientAdapter(provider, LlmConfig.Create("local-model"));

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, StructuredOutputOptions(), CancellationToken.None);

        Assert.Equal(DeliverableGrammar, provider.LastConfig!.GrammarGbnf);
        Assert.Null(provider.LastConfig.ResponseFormat);
    }

    /// <summary>
    /// Without a schema format to fall back on, the grammar is handed to the provider, whose
    /// payload builder drops it with a warning naming <c>Llm:Grammar</c>.
    /// </summary>
    [Fact]
    public async Task StructuredOutput_KeepsTheGrammar_WhenNoSchemaFormatIsDeclared()
    {
        var provider = new CapturingLlmProvider
        {
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonObject },
        };
        using var adapter = new LlmProviderToChatClientAdapter(provider, LlmConfig.Create("json-object-only"));

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, StructuredOutputOptions(), CancellationToken.None);

        Assert.Equal(DeliverableGrammar, provider.LastConfig!.GrammarGbnf);
        Assert.Null(provider.LastConfig.ResponseFormat);
    }

    /// <summary>A response format the crew set itself wins over the deliverable's schema.</summary>
    [Fact]
    public async Task StructuredOutput_NeverOverridesAnExplicitResponseFormat()
    {
        var provider = new CapturingLlmProvider
        {
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonSchema },
        };
        using var adapter = new LlmProviderToChatClientAdapter(provider, LlmConfig.Create("gpt-test"));
        var options = StructuredOutputOptions();
        options.AdditionalProperties![LlmChatOptionsKeys.ResponseFormat] = LlmResponseFormat.JsonObject();

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.Equal("json_object", provider.LastConfig!.ResponseFormat!.Type);
    }

    private sealed class CapturingLlmProvider : ILlmProvider
    {
        public LlmConfig? LastConfig { get; private set; }

        public LlmProviderCapabilities Capabilities { get; init; } = LlmProviderCapabilities.Unknown;

        public string Name => "capturing";

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            LastConfig = config;
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            LastConfig = config;
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }
    }
}
