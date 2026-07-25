using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="LinearRagPipeline"/>: retrieve → assemble → grounded
/// generation → <see cref="RagAnswer"/> with citations and trace.
/// </summary>
public class LinearRagPipelineTests
{
    private static EmbeddedChunk MakeChunk(string id, string content, float[] embedding) => new()
    {
        Chunk = new Chunk
        {
            Id = id,
            DocumentId = "doc-1",
            SourceId = "/kb/doc.txt",
            Content = content,
            StartOffset = 0,
            EndOffset = content.Length,
        },
        Embedding = [.. embedding],
    };

    private static async Task<FakeDocumentStore> SeedStoreAsync(params EmbeddedChunk[] chunks)
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync("kb", chunks, TestContext.Current.CancellationToken);
        return store;
    }

    [Fact]
    public async Task QueryAsync_ReturnsAnswerWithCitationsAndTrace()
    {
        // The fake embedding provider returns [1,1,1,1] for the query: chunk A aligns, chunk B is orthogonal.
        var store = await SeedStoreAsync(
            MakeChunk("a", "alpha content", [1f, 1f, 1f, 1f]),
            MakeChunk("b", "beta content", [-1f, 1f, -1f, 1f]));
        using var chat = new FakeChatClient { ResponseText = "Grounded answer [1].", ResponseModelId = "fake-model" };
        var pipeline = new LinearRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb", TopN = 1 },
            TestContext.Current.CancellationToken);

        Assert.Equal("Grounded answer [1].", answer.Text);

        var citation = Assert.Single(answer.Citations);
        Assert.Equal(1, citation.Marker);
        Assert.Equal("a", citation.ChunkId);
        Assert.Equal("/kb/doc.txt", citation.SourceId);
        Assert.Equal("alpha content", citation.Snippet);
        Assert.True(citation.Score > 0.9); // cosine score survived end-to-end

        Assert.Equal(["retrieve", "assemble", "generate"], answer.Trace.Steps.Select(s => s.Name));
        Assert.Equal("fake-model", answer.Trace.Steps[^1].Data["model"]);
    }

    [Fact]
    public async Task QueryAsync_RetrievalUsesQueryEmbeddingAndCandidateK()
    {
        var store = await SeedStoreAsync(MakeChunk("a", "alpha", [1f, 1f, 1f, 1f]));
        using var chat = new FakeChatClient();
        var pipeline = new LinearRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new LinearRagPipelineOptions { CandidateK = 7 });

        await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        var call = Assert.Single(store.SearchCalls);
        Assert.Equal(7, call.TopK);
        Assert.Equal("alpha?", call.Text);
        Assert.NotNull(call.Embedding);
    }

    [Fact]
    public async Task QueryAsync_ContextSentToChatClient_CarriesNumberedMarkers()
    {
        var store = await SeedStoreAsync(
            MakeChunk("a", "alpha content", [1f, 1f, 1f, 1f]),
            MakeChunk("b", "beta content", [1f, 1f, 1f, 0.9f]));
        using var chat = new FakeChatClient();
        var pipeline = new LinearRagPipeline(store, new FakeEmbeddingProvider(), chat);

        await pipeline.QueryAsync(
            new RagQuery { Text = "everything?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastMessages);
        var system = chat.LastMessages[0];
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Equal(LinearRagPipeline.DefaultSystemPrompt, system.Text);

        var user = chat.LastMessages[^1];
        Assert.Contains("[1]", user.Text, StringComparison.Ordinal);
        Assert.Contains("[2]", user.Text, StringComparison.Ordinal);
        Assert.Contains("alpha content", user.Text, StringComparison.Ordinal);
        Assert.Contains("beta content", user.Text, StringComparison.Ordinal);
        Assert.Contains("everything?", user.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_FiltersArePropagatedToTheStore()
    {
        var store = new FakeDocumentStore();
        using var chat = new FakeChatClient();
        var pipeline = new LinearRagPipeline(store, new FakeEmbeddingProvider(), chat);

        await pipeline.QueryAsync(
            new RagQuery
            {
                Text = "q",
                Collection = "kb",
                Filters = ImmutableDictionary<string, string>.Empty.Add("lang", "fr"),
            },
            TestContext.Current.CancellationToken);

        var call = Assert.Single(store.SearchCalls);
        Assert.Equal("fr", call.Filters["lang"]);
    }

    [Fact]
    public async Task QueryAsync_NoCandidates_SkipsGenerationAndReturnsDeterministicAnswer()
    {
        var store = new FakeDocumentStore(); // empty collection
        using var chat = new FakeChatClient();
        var pipeline = new LinearRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "anything?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        Assert.Equal(LinearRagPipeline.NoContextAnswer, answer.Text);
        Assert.Empty(answer.Citations);
        Assert.Equal(0, chat.CallCount);
        Assert.Contains(answer.Trace.Steps, s => s.Name == "generate" && s.Detail is not null);
    }

    [Fact]
    public async Task QueryAsync_OptionsTemperatureAndMaxTokens_ReachTheChatClient()
    {
        var store = await SeedStoreAsync(MakeChunk("a", "alpha", [1f, 1f, 1f, 1f]));
        using var chat = new FakeChatClient();
        var pipeline = new LinearRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new LinearRagPipelineOptions { Temperature = 0.2f, MaxOutputTokens = 123 });

        await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastOptions);
        Assert.Equal(0.2f, chat.LastOptions.Temperature);
        Assert.Equal(123, chat.LastOptions.MaxOutputTokens);
    }
}
