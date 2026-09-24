using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Evaluation;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Reranking;
using Orkeon.Rag.Routing;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// STUDIO-42, the RAG family: every generation call a RAG pipeline makes — the HyDE probe,
/// the query variants, the listwise rerank, the complexity classifier, the retrieval grade,
/// the grounded answer, the groundedness check — happens under the <c>rag</c> operation, and stays the work of
/// the agent whose tool asked. The host's metered provider does the counting; the pipeline
/// says what the calls are.
/// </summary>
public sealed class RagUsageAttributionTests
{
    private const string Collection = "kb";

    private static EmbeddedChunk MakeChunk(string id, string content) => new()
    {
        Chunk = new Chunk
        {
            Id = id,
            DocumentId = "doc-1",
            SourceId = $"/kb/{id}.txt",
            Content = content,
            StartOffset = 0,
            EndOffset = content.Length,
        },
        Embedding = [1f, 0f, 0f, 0f],
    };

    private static async Task<FakeDocumentStore> SeedStoreAsync()
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync(Collection, [MakeChunk("a", "the warranty lasts two years")], TestContext.Current.CancellationToken);
        return store;
    }

    private static FakeEmbeddingProvider Embeddings() => new() { EmbeddingFunc = _ => [1f, 0f, 0f, 0f] };

    /// <summary>The scope an agent's task opens around its tool calls.</summary>
    private static IDisposable AgentTask() =>
        LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-1", agentId: "Researcher", taskId: "task-1");

    private static void AssertRagWorkOfTheAgent(FakeChatClient chat)
    {
        Assert.Equal(chat.CallCount, chat.Attributions.Count);
        Assert.All(chat.Attributions, attribution =>
        {
            Assert.Equal(LlmUsageOperations.Rag, attribution.Operation);
            Assert.Equal("Researcher", attribution.AgentId);
            Assert.Equal("task-1", attribution.TaskId);
            Assert.Equal("crew-1", attribution.CrewId);
        });
    }

    [Fact]
    public async Task A_staged_query_rewrites_and_answers_as_rag_work_of_the_agent_that_asked()
    {
        var store = await SeedStoreAsync();
        using var chat = new FakeChatClient();
        chat.ScriptedResponses.Enqueue("A hypothetical passage about the warranty.");
        chat.ScriptedResponses.Enqueue("The warranty lasts two years [1].");
        var pipeline = new StagedRagPipeline(
            store, Embeddings(), chat,
            new RagOptions { QueryTransform = new RagQueryTransformOptions { Mode = "hyde" } },
            new StagedRagPipelineDependencies { QueryTransformers = QueryTransformFactoryDefaults.CreateDefault(() => chat) });

        using (AgentTask())
            await pipeline.QueryAsync(new RagQuery { Text = "how long is the warranty?", Collection = Collection }, TestContext.Current.CancellationToken);

        Assert.Equal(2, chat.CallCount);
        AssertRagWorkOfTheAgent(chat);
    }

    [Fact]
    public async Task The_llm_reranker_and_the_answer_are_rag_work()
    {
        var store = await SeedStoreAsync();
        using var chat = new FakeChatClient();
        chat.ScriptedResponses.Enqueue("[1]");
        chat.ScriptedResponses.Enqueue("The warranty lasts two years [1].");
        var rerankers = new RerankerFactory();
        rerankers.Register(LlmListwiseReranker.RerankerName, () => new LlmListwiseReranker(chat));
        var pipeline = new StagedRagPipeline(
            store, Embeddings(), chat,
            new RagOptions { Rerank = new RagRerankOptions { Enabled = true, Kind = LlmListwiseReranker.RerankerName } },
            new StagedRagPipelineDependencies { Rerankers = rerankers });

        using (AgentTask())
            await pipeline.QueryAsync(new RagQuery { Text = "how long is the warranty?", Collection = Collection }, TestContext.Current.CancellationToken);

        Assert.Equal(2, chat.CallCount);
        AssertRagWorkOfTheAgent(chat);
    }

    [Fact]
    public async Task A_retrieval_that_asks_for_query_variants_is_rag_work()
    {
        var store = await SeedStoreAsync();
        using var chat = new FakeChatClient { ResponseText = "1. warranty duration" };
        var pipeline = new StagedRagPipeline(
            store, Embeddings(), chat,
            new RagOptions { QueryTransform = new RagQueryTransformOptions { Mode = "multi-query", VariantCount = 1 } },
            new StagedRagPipelineDependencies { QueryTransformers = QueryTransformFactoryDefaults.CreateDefault(() => chat) });

        using (AgentTask())
            await pipeline.RetrieveAsync(new RagQuery { Text = "warranty?", Collection = Collection }, TestContext.Current.CancellationToken);

        Assert.Equal(1, chat.CallCount);
        AssertRagWorkOfTheAgent(chat);
    }

    [Fact]
    public async Task The_adaptive_router_classifies_and_answers_as_rag_work()
    {
        using var chat = new FakeChatClient();
        chat.ScriptedResponses.Enqueue("""{"route": "no_retrieval"}""");
        chat.ScriptedResponses.Enqueue("A direct answer.");
        var pipeline = new AdaptiveRagPipeline(
            new LlmQueryComplexityClassifier(chat), chat, () => new FakeRagPipeline(), () => new FakeRagPipeline());

        using (AgentTask())
            await pipeline.QueryAsync(new RagQuery { Text = "hello there", Collection = Collection }, TestContext.Current.CancellationToken);

        Assert.Equal(2, chat.CallCount);
        AssertRagWorkOfTheAgent(chat);
    }

    [Fact]
    public async Task The_corrective_graph_grades_answers_and_checks_as_rag_work()
    {
        using var chat = new FakeChatClient();
        chat.ScriptedResponses.Enqueue("""{"grade": "correct", "reason": "on topic"}""");
        chat.ScriptedResponses.Enqueue("The warranty lasts two years [1].");
        chat.ScriptedResponses.Enqueue("""{"grounded": true, "score": 1.0, "unsupported_claims": []}""");
        var store = new StubDocumentStore();
        store.ResultsByCollection[Collection] =
        [
            new ScoredChunk { Chunk = new Chunk { Id = "a", DocumentId = "d-a", SourceId = "s1", Content = "the warranty lasts two years" }, Score = 0.9 },
        ];
        var pipeline = new CorrectiveRagPipeline(
            store, Embeddings(), chat, new LlmRetrievalEvaluator(chat), new RagOptions(),
            new CorrectiveRagPipelineDependencies { GroundednessChecker = new LlmGroundednessChecker(chat) });

        using (AgentTask())
            await pipeline.QueryAsync(new RagQuery { Text = "how long is the warranty?", Collection = Collection }, TestContext.Current.CancellationToken);

        Assert.True(chat.CallCount >= 3, $"expected the grade, the answer and the check, got {chat.CallCount} call(s)");
        AssertRagWorkOfTheAgent(chat);
    }

    [Fact]
    public async Task The_evaluation_judge_is_judge_work()
    {
        using var chat = new FakeChatClient { ResponseText = """{"groundedness": 0.9, "answer_relevance": 0.8}""" };

        await new LlmGenerationJudge(chat).JudgeAsync(
            new RagEvalCase { Id = "c1", Question = "how long is the warranty?" },
            new RagAnswer { Text = "two years [1]" },
            TestContext.Current.CancellationToken);

        var attribution = Assert.Single(chat.Attributions);
        Assert.Equal(LlmUsageOperations.Judge, attribution.Operation);
    }
}
