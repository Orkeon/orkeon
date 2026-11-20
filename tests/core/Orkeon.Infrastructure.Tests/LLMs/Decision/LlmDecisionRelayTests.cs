using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

/// <summary>#430: the host's relay - one chat call on the relay agent's provider, the schema by capability, metered as relay.</summary>
public sealed class LlmDecisionRelayTests : IDisposable
{
    private const string Answer = """{"route":{"bug":0.6,"other":0.4}}""";

    private readonly MockChatClient _client = new();
    private readonly MockLlmProvider _provider = new();

    private static readonly LlmProviderCapabilities WithJsonSchema = new() { ResponseFormat = ResponseFormatSupport.JsonSchema };

    private static DomainAgent Analyst(Action<AgentBuilder>? configure = null)
    {
        var builder = new AgentBuilder().Role("analyst").Goal("Triage the reports");
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static DecisionRelayRequest Request(DomainAgent agent) => new()
    {
        Agent = agent,
        TaskId = "task-1",
        SystemPrompt = "You are analyst.",
        Prompt = "Answer as JSON.",
        PromptWithSchema = "Answer as JSON. The schema: {}",
        Schema = """{"type":"object"}""",
    };

    private LlmDecisionRelay Relay() => new(new StubLlmProfileRegistry(("default", _provider, _client)));

    private System.Threading.Tasks.Task<DecisionRelayResponse> AskAsync(DomainAgent? agent = null) =>
        Relay().AskAsync(Request(agent ?? Analyst()), TestContext.Current.CancellationToken);

    public void Dispose() => _client.Dispose();

    private string? UserPrompt() => _client.LastGetResponseMessages?.Last(m => m.Role == ChatRole.User).Text;

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldSendTheSchemaAsJsonSchema_WhenTheProviderDeclaresIt()
    {
        // Arrange
        _provider.Capabilities = WithJsonSchema;
        _client.SetGetResponseResult(Answer);

        // Act
        var response = await AskAsync();

        // Assert
        var format = Assert.IsType<LlmResponseFormat>(_client.LastGetResponseOptions!.AdditionalProperties![LlmChatOptionsKeys.StructuredOutput]);
        Assert.Equal((false, "Answer as JSON.", """{"type":"object"}"""), (response.SchemaInPrompt, UserPrompt(), format.Schema!.Schema));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldWriteTheSchemaInThePrompt_WhenTheProviderCannotTakeJsonSchema()
    {
        // Arrange
        _client.SetGetResponseResult(Answer);

        // Act
        var response = await AskAsync();

        // Assert
        Assert.Equal(
            (true, "Answer as JSON. The schema: {}", false),
            (response.SchemaInPrompt, UserPrompt(), _client.LastGetResponseOptions!.AdditionalProperties?.ContainsKey(LlmChatOptionsKeys.StructuredOutput) ?? false));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldMeterTheCallAsRelayUnderTheAgentRole_WhenItAsks()
    {
        // Arrange
        LlmUsageAttribution? seen = null;
        _client.SetGetResponseFunc((_, _) =>
        {
            seen = LlmUsageScope.Current;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer));
        });

        // Act
        await AskAsync();

        // Assert
        Assert.Equal((LlmUsageOperations.Relay, "analyst", "task-1"), (seen!.Operation, seen.AgentId, seen.TaskId));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldReturnTheTextAndTheTokens_WhenTheProviderAnswers()
    {
        // Arrange
        _client.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer))
        {
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 15 },
        });

        // Act
        var response = await AskAsync();

        // Assert
        Assert.Equal((Answer, 120, 15), (response.Text, response.InputTokens, response.OutputTokens));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldAskTheAgentsOwnProvider_WhenTheAgentCarriesOne()
    {
        // Arrange
        using var own = new MockChatClient();
        own.SetGetResponseResult(Answer);
        var registry = new StubLlmProfileRegistry(("default", _provider, _client)) { OwnClient = own };
        var agent = Analyst(b => b.WithLlm(new MockLlmProvider()));

        // Act
        await new LlmDecisionRelay(registry).AskAsync(Request(agent), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, 0), (own.GetResponseCallCount, _client.GetResponseCallCount));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldAskTheAgentsProfileWithItsModel_WhenTheAgentNamesOne()
    {
        // Arrange
        using var fast = new MockChatClient();
        fast.SetGetResponseResult(Answer);
        var registry = new StubLlmProfileRegistry(("default", _provider, _client), ("fast", new MockLlmProvider(), fast));
        var agent = Analyst(b => b.WithLlmConfig(LlmConfig.OnProfile("fast") with { Model = "small-1" }));

        // Act
        await new LlmDecisionRelay(registry).AskAsync(Request(agent), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, "small-1"), (fast.GetResponseCallCount, fast.LastGetResponseOptions!.ModelId));
    }
}
