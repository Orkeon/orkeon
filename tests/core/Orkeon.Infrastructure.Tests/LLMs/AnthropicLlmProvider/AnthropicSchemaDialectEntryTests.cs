using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Issue #459: the ways a schema reaches the Anthropic provider without a script's <c>extract</c> - a
/// YAML <c>response_format</c>, and a deliverable's <c>structured_output</c> through the chat-client
/// adapter the crew loop calls - leave closed, the caller's schema unchanged.
/// </summary>
public sealed class AnthropicSchemaDialectEntryTests : IDisposable
{
    private const string OpenSchema = """{"type":"object","properties":{"colour":{"type":"string"}},"required":["colour"]}""";

    private const string MinimalSuccessResponse =
        """{"content":[{"type":"text","text":"{\"colour\":\"red\"}"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly LlmConfig _config;

    public AnthropicSchemaDialectEntryTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with { BaseUrl = new Uri("https://api.anthropic.com") };
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }

    private AnthropicLlmProvider CreateProvider() =>
        new(_config, _httpClientFactory, Policy.NoOpAsync<HttpResponseMessage>());

    private JsonElement SentSchema()
    {
        Assert.Equal(1, _handler.SendCallCount);
        using var doc = JsonDocument.Parse(_handler.LastRequestBody!);
        return doc.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema").Clone();
    }

    [Fact]
    public async Task ShouldSendAClosedSchema_WhenAYamlResponseFormatDeclaresAnOpenOne()
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var yaml = $$"""
name: c
goal: g
agents:
  a:
    role: r
    goal: g
    llm:
      model: claude-opus-5-5
      response_format: json_schema
      response_schema:
        name: colour
        schema: '{{OpenSchema}}'
""";
        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        var format = Assert.Single(config.Agents).LlmConfig!.ResponseFormat!;
        using var provider = CreateProvider();

        var response = await provider.ChatAsync(
            [LlmMessage.User("Name one colour.")], _config with { ResponseFormat = format }, TestContext.Current.CancellationToken);

        Assert.Null(response.Error);
        Assert.Equal(OpenSchema, format.Schema!.Schema);
        Assert.False(SentSchema().GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task ShouldSendAClosedSchema_WhenADeliverableStructuredOutputGoesThroughTheChatClientAdapter()
    {
        using var provider = CreateProvider();
        using var adapter = new LlmProviderToChatClientAdapter(provider, _config);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [LlmChatOptionsKeys.StructuredOutput] = LlmResponseFormat.JsonSchema("structured_output", OpenSchema, strict: false),
            },
        };

        await adapter.GetResponseAsync([new ChatMessage(ChatRole.User, "Name one colour.")], options, TestContext.Current.CancellationToken);

        Assert.False(SentSchema().GetProperty("additionalProperties").GetBoolean());
    }
}
