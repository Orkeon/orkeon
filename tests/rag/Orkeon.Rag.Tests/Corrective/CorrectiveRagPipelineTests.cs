using System.Collections.Immutable;
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Corrective;

/// <summary>
/// Tests for <see cref="CorrectiveRagPipeline"/> (RAG-06/C1): the CRAG graph on
/// <c>StateGraph&lt;RagGraphState&gt;</c> — Correct/Incorrect/Ambiguous routing,
/// the rewrite loop, the groundedness re-loop, the MaxIterations bound (no
/// infinite loop possible — acceptance criterion #2), the circuit breaker
/// degradation, and the opt-in web fallback (skipped and traced otherwise).
/// </summary>
public class CorrectiveRagPipelineTests
{
    private const string Collection = "kb";
    private const string Question = "what is the warranty period?";

    private static RagQuery Query(string text = Question, int topN = 5) =>
        new() { Text = text, Collection = Collection, TopN = topN };

    private static ScoredChunk Scored(string id, string content, double score = 0.9) => new()
    {
        Chunk = new Chunk { Id = id, DocumentId = "d-" + id, SourceId = "s1", Content = content },
        Score = score,
    };

    private static RetrievalVerdict Verdict(RetrievalGrade grade) => new() { Grade = grade };

    private sealed class Harness : IDisposable
    {
        public StubDocumentStore Store { get; } = new();
        public FakeEmbeddingProvider Embeddings { get; } = new();
        public FakeChatClient Chat { get; } = new();
        public StubRetrievalEvaluator Evaluator { get; } = new();
        public StubGroundednessChecker? Checker { get; set; }
        public StubWebDocumentRetriever? Web { get; set; }
        public RagOptions Options { get; } = new();

        public Harness(params ScoredChunk[] chunks)
        {
            Store.ResultsByCollection[Collection] = chunks;
        }

        public CorrectiveRagPipeline Build(CircuitBreakerPolicy? circuitPolicy = null) => new(
            Store, Embeddings, Chat, Evaluator, Options, Checker, Web,
            logger: null, circuitPolicy: circuitPolicy);

        public void Dispose() => Chat.Dispose();
    }

    private static string[] StepNames(RagAnswer answer) =>
        [.. answer.Trace.Steps.Select(s => s.Name)];

    [Fact]
    public async Task CorrectPath_GeneratesCitedAnswer_AndTracesEveryNode()
    {
        using var harness = new Harness(Scored("a", "warranty is two years"), Scored("b", "returns policy"));
        harness.Checker = new StubGroundednessChecker();
        harness.Chat.ResponseText = "The warranty period is two years [1].";

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal("The warranty period is two years [1].", answer.Text);
        Assert.Equal(
            ["corrective:retrieve", "corrective:evaluate", "corrective:generate", "corrective:check_groundedness"],
            StepNames(answer));
        Assert.Equal(0, answer.Trace.Iterations);
        Assert.Empty(answer.Trace.QueryVariants);
        var verdict = Assert.Single(answer.Trace.Verdicts);
        Assert.Equal(RetrievalGrade.Correct, verdict.Grade);
        Assert.NotNull(answer.Groundedness);
        Assert.True(answer.Groundedness.IsGrounded);

        // Rank-based [n] citations, scores preserved end-to-end.
        Assert.Equal([1, 2], answer.Citations.Select(c => c.Marker));
        Assert.Equal(["a", "b"], answer.Citations.Select(c => c.ChunkId));

        // One single LLM call: the generation, on the ORIGINAL question.
        Assert.Equal(1, harness.Chat.CallCount);
        Assert.Equal(StagedRagPipeline.DefaultSystemPrompt, harness.Chat.LastMessages![0].Text);
        Assert.Contains("Question: " + Question, harness.Chat.LastMessages[1].Text, StringComparison.Ordinal);
        Assert.Contains("[1] (source: s1)", harness.Chat.LastMessages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncorrectThenCorrect_RewritesTheQuery_AndLoopsBackToRetrieve()
    {
        using var harness = new Harness(Scored("a", "guarantee lasts twenty-four months"));
        harness.Evaluator.ScriptedVerdicts.Enqueue(Verdict(RetrievalGrade.Incorrect));
        harness.Evaluator.ScriptedVerdicts.Enqueue(Verdict(RetrievalGrade.Correct));
        harness.Chat.ScriptedResponses.Enqueue("guarantee duration in months");
        harness.Chat.ScriptedResponses.Enqueue("The guarantee lasts 24 months [1].");

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal("The guarantee lasts 24 months [1].", answer.Text);
        Assert.Equal(
            ["corrective:retrieve", "corrective:evaluate", "corrective:rewrite_query",
             "corrective:retrieve", "corrective:evaluate", "corrective:generate",
             "corrective:check_groundedness"],
            StepNames(answer));
        Assert.Equal(1, answer.Trace.Iterations);
        Assert.Equal(["guarantee duration in months"], answer.Trace.QueryVariants);

        // The second retrieval probed the REWRITTEN query…
        Assert.Equal(2, harness.Store.SearchCalls.Count);
        Assert.Equal(Question, harness.Store.SearchCalls[0].Query.Text);
        Assert.Equal("guarantee duration in months", harness.Store.SearchCalls[1].Query.Text);

        // …while the evaluation and the generation used the ORIGINAL question.
        Assert.All(harness.Evaluator.Calls, call => Assert.Equal(Question, call.Query));
        Assert.Contains("Question: " + Question, harness.Chat.LastMessages![1].Text, StringComparison.Ordinal);

        // No checker registered: the stage is skipped and traced, never fails.
        var check = answer.Trace.Steps[^1];
        Assert.Contains("no IGroundednessChecker registered", check.Detail, StringComparison.Ordinal);
        Assert.Null(answer.Groundedness);
    }

    [Fact]
    public async Task Ambiguous_Refines_FiltersChunksByRelevance_AndKeepsRelevantSegments()
    {
        using var harness = new Harness(
            Scored("c1", "The warranty period is two years. Bananas are yellow."),
            Scored("c2", "Totally unrelated text about dogs."));
        harness.Evaluator.ScriptedVerdicts.Enqueue(new RetrievalVerdict
        {
            Grade = RetrievalGrade.Ambiguous,
            ChunkRelevances =
            [
                new ChunkRelevance { ChunkId = "c1", Relevance = 0.9 },
                new ChunkRelevance { ChunkId = "c2", Relevance = 0.1 },
            ],
        });
        harness.Chat.ResponseText = "Two years [1].";

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["corrective:retrieve", "corrective:evaluate", "corrective:refine",
             "corrective:generate", "corrective:check_groundedness"],
            StepNames(answer));

        // c2 was filtered out (relevance 0.1 < 0.5), and c1 was re-split: only
        // the warranty segment survived the decompose-then-recompose.
        var citation = Assert.Single(answer.Citations);
        Assert.Equal("c1", citation.ChunkId);
        var prompt = harness.Chat.LastMessages![1].Text;
        Assert.Contains("The warranty period is two years", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Bananas", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("dogs", prompt, StringComparison.Ordinal);

        var refine = answer.Trace.Steps[2];
        Assert.Equal("2", refine.Data["in"]);
        Assert.Equal("1", refine.Data["out"]);
    }

    [Fact]
    public async Task UngroundedAnswer_ReloopsThroughRewrite_ThenEndsGrounded()
    {
        using var harness = new Harness(Scored("a", "warranty is two years"));
        harness.Checker = new StubGroundednessChecker();
        harness.Checker.ScriptedResults.Enqueue(new GroundednessResult
        {
            IsGrounded = false,
            Score = 0.2,
            UnsupportedClaims = ["ten-year warranty"],
        });
        harness.Checker.ScriptedResults.Enqueue(new GroundednessResult { IsGrounded = true, Score = 0.95 });
        harness.Chat.ScriptedResponses.Enqueue("hallucinated answer [1]"); // generate #1
        harness.Chat.ScriptedResponses.Enqueue("warranty duration probe");  // rewrite
        harness.Chat.ScriptedResponses.Enqueue("grounded answer [1]");      // generate #2

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal("grounded answer [1]", answer.Text);
        Assert.Equal(1, answer.Trace.Iterations);
        Assert.Equal(
            ["corrective:retrieve", "corrective:evaluate", "corrective:generate",
             "corrective:check_groundedness", "corrective:rewrite_query",
             "corrective:retrieve", "corrective:evaluate", "corrective:generate",
             "corrective:check_groundedness"],
            StepNames(answer));
        Assert.NotNull(answer.Groundedness);
        Assert.True(answer.Groundedness.IsGrounded);

        // The rewrite prompt carried the unsupported claims to cover.
        var rewritePrompt = harness.Chat.Calls[1][1].Text;
        Assert.Contains("ten-year warranty", rewritePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaxIterationsReached_TerminatesCleanly_GeneratesBestEffort_AndTracesExhaustion()
    {
        // Acceptance criterion #2: no infinite loop possible. The evaluator
        // ALWAYS grades Incorrect — the loop must stop at MaxIterations.
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 2;
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["corrective:retrieve", "corrective:evaluate", "corrective:rewrite_query",
             "corrective:retrieve", "corrective:evaluate", "corrective:rewrite_query",
             "corrective:retrieve", "corrective:evaluate", "corrective:generate",
             "corrective:check_groundedness"],
            StepNames(answer));
        Assert.Equal(2, answer.Trace.Iterations);
        Assert.Equal(3, answer.Trace.Verdicts.Count);

        // Exhaustion is traced explicitly, and generation still produced an answer.
        var lastEvaluate = answer.Trace.Steps[7];
        Assert.Contains("iteration budget exhausted (2/2)", lastEvaluate.Detail, StringComparison.Ordinal);
        Assert.Contains("web_fallback skipped (disabled)", lastEvaluate.Detail, StringComparison.Ordinal);
        var generate = answer.Trace.Steps[8];
        Assert.Contains("best-effort", generate.Detail, StringComparison.Ordinal);
        Assert.Equal("fake answer", answer.Text);
        Assert.NotEmpty(answer.Citations);
    }

    [Fact]
    public async Task UngroundedForever_TerminatesAtMaxIterations_WithExplicitTrace()
    {
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 1;
        harness.Checker = new StubGroundednessChecker
        {
            DefaultResult = new GroundednessResult { IsGrounded = false, Score = 0.1 },
        };

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(1, answer.Trace.Iterations);
        Assert.NotNull(answer.Groundedness);
        Assert.False(answer.Groundedness.IsGrounded);
        var lastCheck = answer.Trace.Steps[^1];
        Assert.Equal("corrective:check_groundedness", lastCheck.Name);
        Assert.Contains("iteration budget exhausted (1/1)", lastCheck.Detail, StringComparison.Ordinal);
        Assert.Contains("best-effort", lastCheck.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CircuitBreaker_Trips_DegradesToBestEffortAnswer_NeverThrows()
    {
        // Second protection layer: even with a huge MaxIterations, an explicit
        // (here: pathologically tight) circuit breaker stops the graph — and the
        // pipeline degrades to a best-effort answer instead of surfacing the
        // GraphCircuitBrokenException.
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 10;
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);
        var tightPolicy = new CircuitBreakerPolicy
        {
            MaxTransitions = 3,
            MaxStateVisits = 0,           // disabled — force the transition trip
            MaxTotalDuration = TimeSpan.Zero,
        };

        var answer = await harness.Build(tightPolicy).QueryAsync(Query(), TestContext.Current.CancellationToken);

        var breakerStep = Assert.Single(answer.Trace.Steps, s => s.Name == "corrective:circuit_breaker");
        Assert.Contains("best-effort", breakerStep.Detail, StringComparison.Ordinal);
        Assert.Equal("3", breakerStep.Data["transitions"]);

        // The degraded path still generated with the best available chunks.
        Assert.Equal("corrective:generate", answer.Trace.Steps[^1].Name);
        Assert.Equal("fake answer", answer.Text);
        Assert.NotEmpty(answer.Citations);
    }

    [Fact]
    public async Task DefaultCircuitPolicy_IsDerivedFromTheIterationBudget()
    {
        var policy = CorrectiveRagPipeline.BuildCircuitPolicy(maxIterations: 3);

        Assert.Equal(35, policy.MaxTransitions);
        Assert.Equal(5, policy.MaxStateVisits);
        Assert.True(policy.MaxTotalDuration > TimeSpan.Zero);

        // And the derived policy never trips a legitimate exhaustion run.
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 3;
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(answer.Trace.Steps, s => s.Name == "corrective:circuit_breaker");
        Assert.Equal(3, answer.Trace.Iterations);
    }

    [Fact]
    public async Task WebFallback_EnabledButNoRetrieverRegistered_IsSkippedAndTraced()
    {
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 0;
        harness.Options.Corrective.WebFallback.Enabled = true;
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(answer.Trace.Steps, s => s.Name == "corrective:web_fallback");
        var evaluate = Assert.Single(answer.Trace.Steps, s => s.Name == "corrective:evaluate");
        Assert.Contains("web_fallback skipped (no IWebDocumentRetriever registered)",
            evaluate.Detail, StringComparison.Ordinal);
        Assert.Equal("fake answer", answer.Text);
    }

    [Fact]
    public async Task WebFallback_RetrieverRegisteredButDisabled_IsSkippedAndTraced()
    {
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 0;
        harness.Web = new StubWebDocumentRetriever
        {
            Results = { new RagDocument { Id = "w1", SourceId = "web", Content = "web content" } },
        };
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Empty(harness.Web.Calls);
        var evaluate = Assert.Single(answer.Trace.Steps, s => s.Name == "corrective:evaluate");
        Assert.Contains("web_fallback skipped (disabled)", evaluate.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebFallback_EnabledAndRegistered_FiresAfterRewriteExhaustion_AndOpensTheContext()
    {
        using var harness = new Harness(Scored("a", "irrelevant local content"));
        harness.Options.Corrective.MaxIterations = 0;
        harness.Options.Corrective.WebFallback.Enabled = true;
        harness.Options.Corrective.WebFallback.MaxResults = 2;
        harness.Web = new StubWebDocumentRetriever
        {
            Results = { new RagDocument { Id = "w1", SourceId = "web", Content = "the warranty period is two years" } },
        };
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);
        harness.Chat.ResponseText = "Two years [1].";

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["corrective:retrieve", "corrective:evaluate", "corrective:web_fallback",
             "corrective:generate", "corrective:check_groundedness"],
            StepNames(answer));
        var webCall = Assert.Single(harness.Web.Calls);
        Assert.Equal(Question, webCall.Query);
        Assert.Equal(2, webCall.MaxResults);

        var webStep = answer.Trace.Steps[2];
        Assert.Equal("1", webStep.Data["results"]);

        // Web results open the working set: [1] cites the web chunk.
        Assert.Equal("web:w1", answer.Citations[0].ChunkId);
        Assert.Equal("web", answer.Citations[0].SourceId);
    }

    [Fact]
    public async Task NoContextRetrieved_ReturnsDeterministicAnswer_WithoutAnyLlmCall()
    {
        using var harness = new Harness(); // empty store
        harness.Checker = new StubGroundednessChecker();

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(StagedRagPipeline.NoContextAnswer, answer.Text);
        Assert.Empty(answer.Citations);
        Assert.Equal(0, harness.Chat.CallCount);
        Assert.Empty(harness.Checker.Calls);
        var check = answer.Trace.Steps[^1];
        Assert.Contains("deterministic no-context answer", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RewriteProducingNoUsableText_RetriesThePreviousProbe_AndStillAdvancesTheBudget()
    {
        using var harness = new Harness(Scored("a", "some content"));
        harness.Options.Corrective.MaxIterations = 1;
        harness.Evaluator.DefaultVerdict = Verdict(RetrievalGrade.Incorrect);
        harness.Chat.ScriptedResponses.Enqueue("   \n  "); // unusable rewrite
        harness.Chat.ScriptedResponses.Enqueue("best effort answer");

        var answer = await harness.Build().QueryAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(1, answer.Trace.Iterations);
        Assert.Empty(answer.Trace.QueryVariants);
        var rewrite = Assert.Single(answer.Trace.Steps, s => s.Name == "corrective:rewrite_query");
        Assert.Contains("no usable text", rewrite.Detail, StringComparison.Ordinal);
        Assert.Equal(Question, harness.Store.SearchCalls[1].Query.Text);
        Assert.Equal("best effort answer", answer.Text);
    }
}
