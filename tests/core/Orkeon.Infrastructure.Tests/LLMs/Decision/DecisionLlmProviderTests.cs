using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Decision;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

public class DecisionLlmProviderTests
{
    private readonly StubLlmProvider _target = new StubLlmProvider { Name = "cheap" }
        .RespondToChatWith(new LlmResponse { Content = "cheap answer" })
        .RespondWith(new LlmResponse { Content = "cheap text" });

    private readonly StubLlmProvider _strong = new StubLlmProvider { Name = "strong" }
        .RespondToChatWith(new LlmResponse { Content = "strong answer" });

    private static readonly LlmMessage[] Conversation = [LlmMessage.User("Hello?")];

    private DecisionLlmProvider Create(
        IDecisionStage[]? request = null,
        IDecisionStage[]? response = null,
        bool failClosed = false,
        MockAuditLogger? audit = null) =>
        new(
            "triage",
            _target,
            request ?? [],
            response ?? [],
            name => name == "strong" ? _strong : throw new InvalidOperationException(name),
            failClosed,
            NullLogger.Instance,
            audit);

    [Fact]
    public async Task ShouldCallTheTarget_WhenEveryStageContinues()
    {
        var stage = new StubDecisionStage();
        var provider = Create([stage], [stage]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cheap answer", response.Content);
        Assert.Equal(1, stage.RequestCalls);
        Assert.Equal(1, stage.ResponseCalls);
        Assert.Equal("cheap", provider.Name);
    }

    [Fact]
    public async Task ShouldAnswerAStructuredErrorWithoutCalling_WhenARequestStageBlocks()
    {
        var stage = new StubDecisionStage { Name = "guard", OnRequest = _ => DecisionVerdict.Block("prompt injection") };
        var provider = Create([stage]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(_target.ChatCalls);
        Assert.Equal(DecisionLlmProvider.BlockedErrorType, response.ErrorType);
        Assert.Contains("guard", response.Error, StringComparison.Ordinal);
        Assert.Contains("prompt injection", response.Error, StringComparison.Ordinal);
        Assert.Equal("guard", response.Metadata[DecisionLlmProvider.StageMetadataKey]);
    }

    [Fact]
    public async Task ShouldCallTheRoutedProfile_WhenARequestStageRoutes()
    {
        var provider = Create([new StubDecisionStage { OnRequest = _ => DecisionVerdict.Route("strong") }]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("strong answer", response.Content);
        Assert.Empty(_target.ChatCalls);
    }

    [Fact]
    public async Task ShouldReplaceTheAnswerWithAnError_WhenAResponseStageBlocks()
    {
        var provider = Create(response: [new StubDecisionStage { OnResponse = _ => DecisionVerdict.Block("leaks a secret") }]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(_target.ChatCalls);
        Assert.Equal(string.Empty, response.Content);
        Assert.Equal(DecisionLlmProvider.BlockedErrorType, response.ErrorType);
    }

    [Fact]
    public async Task ShouldEscalateOnce_WhenAResponseStageKeepsEscalating()
    {
        var stage = new StubDecisionStage { OnResponse = _ => DecisionVerdict.Escalate("strong") };
        var provider = Create(response: [stage]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("strong answer", response.Content);
        Assert.Single(_target.ChatCalls);
        Assert.Single(_strong.ChatCalls);
        Assert.Equal(2, stage.ResponseCalls);
    }

    [Fact]
    public async Task ShouldJudgeTheEscalatedAnswer_WhenAStageBlocksIt()
    {
        var verify = new StubDecisionStage { OnResponse = r => r.Content == "cheap answer" ? DecisionVerdict.Escalate("strong") : DecisionVerdict.Continue };
        var guard = new StubDecisionStage { OnResponse = r => r.Content == "strong answer" ? DecisionVerdict.Block("toxic") : DecisionVerdict.Continue };
        var provider = Create(response: [verify, guard]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DecisionLlmProvider.BlockedErrorType, response.ErrorType);
    }

    [Fact]
    public async Task ShouldSkipTheResponseStages_WhenTheModelCallFailed()
    {
        _target.RespondToChatWith(new LlmResponse
        {
            Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = "timeout" },
        });
        var stage = new StubDecisionStage { OnResponse = _ => DecisionVerdict.Escalate("strong") };
        var provider = Create(response: [stage]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("timeout", response.Error);
        Assert.Equal(0, stage.ResponseCalls);
    }

    [Fact]
    public async Task ShouldGoOn_WhenAStageFailsAndTheProfileIsFailOpen()
    {
        var provider = Create([new StubDecisionStage { OnRequest = _ => throw new DecisionModelException("529") }]);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cheap answer", response.Content);
    }

    [Fact]
    public async Task ShouldBlock_WhenAStageFailsAndTheProfileIsFailClosed()
    {
        var provider = Create([new StubDecisionStage { OnRequest = _ => throw new DecisionModelException("529") }], failClosed: true);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(_target.ChatCalls);
        Assert.Equal(DecisionLlmProvider.BlockedErrorType, response.ErrorType);
        Assert.Contains("fail-closed", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAuditEachVerdictButContinue_WhenAnAuditLoggerIsGiven()
    {
        var audit = new MockAuditLogger();
        var provider = Create(
            [new StubDecisionStage(), new StubDecisionStage { Name = "route", OnRequest = _ => DecisionVerdict.Route("strong", "hard") }],
            audit: audit);

        await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        var logged = Assert.Single(audit.AllLoggedEvents);
        Assert.Equal("decision.route", logged.Action);
        Assert.Equal("strong", logged.Details["to"]);
        Assert.Equal("route", logged.Details["stage"]);
    }

    [Fact]
    public async Task ShouldJudgeAPromptAsAUserMessage_WhenGenerateAsync()
    {
        LlmMessage[]? seen = null;
        var provider = Create([new StubDecisionStage { OnRequest = m => { seen = m; return DecisionVerdict.Continue; } }]);

        var response = await provider.GenerateAsync("Summarise", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cheap text", response.Content);
        Assert.Equal("Summarise", Assert.Single(seen!).Content);
        Assert.Single(_target.GenerateCalls);
    }

    [Fact]
    public async Task ShouldBufferTheStream_WhenAResponseStageJudgesIt()
    {
        var provider = Create(response: [new StubDecisionStage { OnResponse = _ => DecisionVerdict.Escalate("strong") }]);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(ev);

        Assert.Equal("strong answer", events[0].Delta);
        Assert.Equal("strong answer", events[^1].FinalResponse!.Content);
        Assert.False(provider.StreamsNatively);
    }

    [Fact]
    public async Task ShouldEndTheStreamOnTheError_WhenARequestStageBlocks()
    {
        var provider = Create([new StubDecisionStage { OnRequest = _ => DecisionVerdict.Block("no") }]);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(ev);

        Assert.Equal(DecisionLlmProvider.BlockedErrorType, Assert.Single(events).FinalResponse!.ErrorType);
    }

    [Fact]
    public async Task ShouldThrowOnATextStream_WhenAStageBlocks()
    {
        var provider = Create([new StubDecisionStage { OnRequest = _ => DecisionVerdict.Block("no") }]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("x", cancellationToken: TestContext.Current.CancellationToken))
            {
                // Drains the stream: the throw is what the test checks.
            }
        });
    }
}
