using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="AdaptiveRagPipeline"/> (RAG-05/C3): the classifier
/// routes each query — <c>NoRetrieval</c> answers directly (no retrieval, empty
/// citations), <c>SingleShot</c> delegates to the balanced pipeline,
/// <c>Iterative</c> falls back to quality (documented, RAG-06 pending) — and
/// the decision is always traced (<c>RagTrace.Route</c> + <c>route</c> step).
/// </summary>
public class AdaptiveRagPipelineTests
{
    private static RagQuery Query(string text = "what is the warranty period?") =>
        new() { Text = text, Collection = "kb" };

    private sealed class Harness : IDisposable
    {
        public StubQueryComplexityClassifier Classifier { get; }
        public FakeChatClient Chat { get; }
        public FakeRagPipeline SingleShot { get; }
        public FakeRagPipeline Iterative { get; }
        public AdaptiveRagPipeline Pipeline { get; }

        public Harness(QueryRoute route)
        {
            Classifier = new StubQueryComplexityClassifier { Route = route };
            Chat = new FakeChatClient { ResponseText = "direct answer", ResponseModelId = "fake-model" };
            SingleShot = new FakeRagPipeline
            {
                DefaultAnswer = new RagAnswer
                {
                    Text = "balanced answer [1]",
                    Trace = new RagTrace
                    {
                        Steps = [new RagTraceStep { Name = "retrieve" }],
                    },
                },
            };
            Iterative = new FakeRagPipeline { DefaultAnswer = new RagAnswer { Text = "quality answer [1]" } };
            Pipeline = new AdaptiveRagPipeline(Classifier, Chat, () => SingleShot, () => Iterative);
        }

        public void Dispose() => Chat.Dispose();
    }

    private static Harness CreateHarness(QueryRoute route) => new(route);

    [Fact]
    public async Task NoRetrieval_AnswersDirectly_WithEmptyCitations_AndTracesTheRoute()
    {
        using var harness = CreateHarness(QueryRoute.NoRetrieval);

        var answer = await harness.Pipeline.QueryAsync(
            Query("hello there"), TestContext.Current.CancellationToken);

        Assert.Equal("direct answer", answer.Text);
        Assert.Empty(answer.Citations);
        Assert.Equal(QueryRoute.NoRetrieval, answer.Trace.Route);

        // No delegation happened — the LLM answered without any retrieval.
        Assert.Empty(harness.SingleShot.Queries);
        Assert.Empty(harness.Iterative.Queries);
        Assert.Equal(1, harness.Chat.CallCount);
        Assert.Equal("hello there", Assert.Single(harness.Classifier.Queries));

        Assert.Equal(["route", "generate"], answer.Trace.Steps.Select(s => s.Name));
        var routeStep = answer.Trace.Steps[0];
        Assert.Equal("NoRetrieval", routeStep.Data["route"]);
        Assert.Equal(nameof(StubQueryComplexityClassifier), routeStep.Data["classifier"]);
        Assert.False(routeStep.Data.ContainsKey("delegate"));
        Assert.Contains("no retrieval", answer.Trace.Steps[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoRetrieval_UsesTheDirectAnswerSystemPrompt()
    {
        using var harness = CreateHarness(QueryRoute.NoRetrieval);

        await harness.Pipeline.QueryAsync(Query("thanks!"), TestContext.Current.CancellationToken);

        Assert.Equal(
            AdaptiveRagPipeline.DirectAnswerSystemPrompt,
            harness.Chat.LastMessages![0].Text);
        Assert.Equal("thanks!", harness.Chat.LastMessages[1].Text);
    }

    [Fact]
    public async Task SingleShot_DelegatesToTheBalancedPipeline_AndPrependsTheRouteStep()
    {
        using var harness = CreateHarness(QueryRoute.SingleShot);

        var answer = await harness.Pipeline.QueryAsync(
            Query(), TestContext.Current.CancellationToken);

        Assert.Equal("balanced answer [1]", answer.Text);
        Assert.Equal(QueryRoute.SingleShot, answer.Trace.Route);

        // The ORIGINAL query object reached the delegate untouched.
        var delegated = Assert.Single(harness.SingleShot.Queries);
        Assert.Equal("what is the warranty period?", delegated.Text);
        Assert.Empty(harness.Iterative.Queries);
        Assert.Equal(0, harness.Chat.CallCount);

        // The route step is prepended to the delegate's own trace.
        Assert.Equal(["route", "retrieve"], answer.Trace.Steps.Select(s => s.Name));
        Assert.Equal("SingleShot", answer.Trace.Steps[0].Data["route"]);
        Assert.Equal("balanced", answer.Trace.Steps[0].Data["delegate"]);
    }

    [Fact]
    public async Task Iterative_FallsBackToTheQualityPipeline_AndDocumentsTheFallbackInTheTrace()
    {
        using var harness = CreateHarness(QueryRoute.Iterative);

        var answer = await harness.Pipeline.QueryAsync(
            Query("compare A versus B?"), TestContext.Current.CancellationToken);

        Assert.Equal("quality answer [1]", answer.Text);
        Assert.Equal(QueryRoute.Iterative, answer.Trace.Route);
        Assert.Single(harness.Iterative.Queries);
        Assert.Empty(harness.SingleShot.Queries);

        var routeStep = answer.Trace.Steps[0];
        Assert.Equal("route", routeStep.Name);
        Assert.Equal("Iterative", routeStep.Data["route"]);
        Assert.Equal("quality", routeStep.Data["delegate"]);
        Assert.Contains("RAG-06", routeStep.Detail, StringComparison.Ordinal);
        Assert.Contains("quality", routeStep.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DelegatePipelines_AreResolvedLazily_OnlyOnTheirRoute()
    {
        var classifier = new StubQueryComplexityClassifier { Route = QueryRoute.NoRetrieval };
        using var chat = new FakeChatClient();
        var pipeline = new AdaptiveRagPipeline(
            classifier,
            chat,
            () => throw new InvalidOperationException("single-shot must not be resolved"),
            () => throw new InvalidOperationException("iterative must not be resolved"));

        var answer = await pipeline.QueryAsync(Query("hi"), TestContext.Current.CancellationToken);

        Assert.Equal(QueryRoute.NoRetrieval, answer.Trace.Route);
    }

    [Fact]
    public async Task QueryAsync_GuardsArguments()
    {
        using var harness = CreateHarness(QueryRoute.SingleShot);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Pipeline.QueryAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Pipeline.QueryAsync(
                new RagQuery { Text = "  ", Collection = "kb" },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Ctor_GuardsCollaborators()
    {
        var classifier = new StubQueryComplexityClassifier();
        using var chat = new FakeChatClient();
        static Orkeon.Rag.Abstractions.Interfaces.IRagPipeline Fake() => new FakeRagPipeline();

        Assert.Throws<ArgumentNullException>(() => new AdaptiveRagPipeline(null!, chat, Fake, Fake));
        Assert.Throws<ArgumentNullException>(() => new AdaptiveRagPipeline(classifier, null!, Fake, Fake));
        Assert.Throws<ArgumentNullException>(() => new AdaptiveRagPipeline(classifier, chat, null!, Fake));
        Assert.Throws<ArgumentNullException>(() => new AdaptiveRagPipeline(classifier, chat, Fake, null!));
    }
}
