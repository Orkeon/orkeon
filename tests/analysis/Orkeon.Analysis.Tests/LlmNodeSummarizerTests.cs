using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Summarizers;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Analysis.Tests;

public class LlmNodeSummarizerTests
{
    [Fact]
    public async Task NullSummarizer_produces_fallback_summary()
    {
        var node = MakeNode("UserService", UniversalNodeKind.Class, "class UserService { /* long enough body ........... */ }");
        var context = new SummarizationContext(null, [], [], null);
        var summary = await NullNodeSummarizer.Instance.SummarizeAsync(node, context, CancellationToken.None);
        Assert.NotNull(summary);
        Assert.Contains("UserService", summary);
    }

    [Fact]
    public async Task LlmSummarizer_skips_small_snippets()
    {
        var llm = new CountingLlmProvider("summary text");
        var summarizer = new LlmNodeSummarizer(llm);
        var small = MakeNode("tiny", UniversalNodeKind.Method, "a()");
        var context = new SummarizationContext(null, [], [], null);
        var summary = await summarizer.SummarizeAsync(small, context, CancellationToken.None);
        Assert.Null(summary);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task LlmSummarizer_skips_non_supported_kinds()
    {
        var llm = new CountingLlmProvider("x");
        var summarizer = new LlmNodeSummarizer(llm);
        var field = MakeNode("field", UniversalNodeKind.Field, new string('x', 200));
        var context = new SummarizationContext(null, [], [], null);
        var summary = await summarizer.SummarizeAsync(field, context, CancellationToken.None);
        Assert.Null(summary);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task LlmSummarizer_uses_llm_and_caches()
    {
        var llm = new CountingLlmProvider("Manages users.");
        var summarizer = new LlmNodeSummarizer(llm);
        var node = MakeNode("UserService", UniversalNodeKind.Class, new string('x', 200));
        var context = new SummarizationContext(null, [], [], null);

        var s1 = await summarizer.SummarizeAsync(node, context, CancellationToken.None);
        var s2 = await summarizer.SummarizeAsync(node, context, CancellationToken.None);

        Assert.Equal("Manages users.", s1);
        Assert.Equal(s1, s2);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task EnrichBatchAsync_writes_summary_to_nodes()
    {
        var llm = new CountingLlmProvider("Description.");
        var summarizer = new LlmNodeSummarizer(llm);
        var nodes = new List<RaggableNode>
        {
            MakeNode("A", UniversalNodeKind.Class, new string('x', 200), "class A"),
            MakeNode("B", UniversalNodeKind.Method, new string('y', 200), "B()"),
            MakeNode("C", UniversalNodeKind.Field, new string('z', 200), "field C"),
        };
        var index = nodes.ToDictionary(n => n.Fqn, n => n);

        await summarizer.EnrichBatchAsync(nodes, index, CancellationToken.None);

        Assert.Equal("Description.", nodes[0].SemanticSummary);
        Assert.Equal("Description.", nodes[1].SemanticSummary);
        Assert.Null(nodes[2].SemanticSummary);
        Assert.Equal(2, llm.Calls);
    }

    [Fact]
    public async Task EnrichBatchAsync_concurrency_respects_limit()
    {
        var llm = new ConcurrencyTrackingLlmProvider();
        var summarizer = new LlmNodeSummarizer(llm, new LlmNodeSummarizerOptions { Concurrency = 2 });
        var nodes = Enumerable.Range(0, 10)
            .Select(i => MakeNode("N" + i, UniversalNodeKind.Function, new string('x', 200)))
            .ToList();
        var index = nodes.ToDictionary(n => n.Fqn, n => n);

        await summarizer.EnrichBatchAsync(nodes, index, CancellationToken.None);

        Assert.True(llm.MaxConcurrent <= 2, $"max concurrent was {llm.MaxConcurrent}");
        Assert.All(nodes, n => Assert.NotNull(n.SemanticSummary));
    }

    private static RaggableNode MakeNode(string name, UniversalNodeKind kind, string snippet, string? signature = null)
    {
        var node = new RaggableNode
        {
            Id = "id::" + name,
            Kind = kind,
            Name = name,
            VirtualFilePath = $"/tmp/{name}.ts",
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = snippet,
            Sha256 = string.Empty,
            Fqn = "fqn::" + name,
        };
        if (signature is not null) node.Signature = signature;
        return node;
    }

    private sealed class CountingLlmProvider : ILlmProvider
    {
        private readonly string _response;
        public int Calls;
        public CountingLlmProvider(string response) { _response = response; }
        public string Name => "fake";
        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new LlmResponse { Content = _response });
        }
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => GenerateAsync(messages[^1].Content, config, cancellationToken);
    }

    private sealed class ConcurrencyTrackingLlmProvider : ILlmProvider
    {
        private int _current;
        public int MaxConcurrent;
        public string Name => "track";
        public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _current);
            try
            {
                int prev;
                do
                {
                    prev = MaxConcurrent;
                    if (now <= prev) break;
                } while (Interlocked.CompareExchange(ref MaxConcurrent, now, prev) != prev);
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                return new LlmResponse { Content = "summary" };
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => GenerateAsync(messages[^1].Content, config, cancellationToken);
    }
}
