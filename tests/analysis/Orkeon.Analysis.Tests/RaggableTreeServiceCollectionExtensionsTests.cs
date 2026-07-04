using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Analysis.Summarizers;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis;
using Orkeon.Tools.Analysis.DependencyInjection;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRaggableTree_registers_core_singletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IFileSystemDiscoverer>());
        Assert.NotNull(provider.GetService<TreeSitterParserPool>());
        Assert.NotNull(provider.GetService<IReferenceResolver>());
        Assert.NotNull(provider.GetService<IEmbeddingTextComposer>());
    }

    [Fact]
    public void AddRaggableTree_registers_five_language_adapters()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();

        using var provider = services.BuildServiceProvider();
        var adapters = provider.GetServices<ILanguageAdapter>().ToList();

        Assert.Equal(5, adapters.Count);
        Assert.Contains(adapters, a => a.LanguageName == "typescript");
        Assert.Contains(adapters, a => a.LanguageName == "python");
        Assert.Contains(adapters, a => a.LanguageName == "csharp");
        Assert.Contains(adapters, a => a.LanguageName == "go");
        Assert.Contains(adapters, a => a.LanguageName == "rust");
    }

    [Fact]
    public void AddRaggableTree_registers_five_fingerprinters()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();

        using var provider = services.BuildServiceProvider();
        var fingerprinters = provider.GetServices<IFrameworkFingerprinter>().ToList();

        Assert.Equal(5, fingerprinters.Count);
        var names = fingerprinters.Select(f => f.Name).ToHashSet();
        Assert.Contains("angular", names);
        Assert.Contains("nestjs", names);
        Assert.Contains("aspnet", names);
        Assert.Contains("flask", names);
        Assert.Contains("fastapi", names);
    }

    [Fact]
    public void AddRaggableTree_uses_NullNodeSummarizer_when_enrichment_disabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions { EnrichWithLlm = false });

        using var provider = services.BuildServiceProvider();
        var summarizer = provider.GetRequiredService<INodeSummarizer>();

        Assert.IsType<NullNodeSummarizer>(summarizer);
    }

    [Fact]
    public void AddRaggableTree_uses_LlmNodeSummarizer_when_enrichment_enabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILlmProvider, StubLlm>();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            EnrichWithLlm = true,
            Summarizer = new SummarizerOptions
            {
                Provider = SummarizerProviderKind.Anthropic,
                Model = "claude-haiku-4-5",
            },
        });

        using var provider = services.BuildServiceProvider();
        var summarizer = provider.GetRequiredService<INodeSummarizer>();

        Assert.IsType<LlmNodeSummarizer>(summarizer);
    }

    [Fact]
    public void AddRaggableTree_skips_embedding_provider_when_none()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.None },
        });

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IEmbeddingProvider>());
    }

    [Fact]
    public void AddRaggableTree_registers_openai_embedding_provider_when_configured()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.OpenAI,
                ApiKey = "sk-test",
                Model = "text-embedding-3-small",
            },
        });

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetService<IEmbeddingProvider>();

        Assert.NotNull(embedder);
        Assert.IsType<OpenAIEmbeddingProvider>(embedder);
    }

    [Fact]
    public void AddRaggableTree_registers_ollama_embedding_provider_when_configured()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.Ollama,
                Model = "nomic-embed-text",
            },
        });

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetService<IEmbeddingProvider>();

        Assert.NotNull(embedder);
        Assert.IsType<OllamaEmbeddingProvider>(embedder);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddRaggableTree_wires_queryEmbedder_when_embedding_provider_registered()
    {
        // Reset the shared static counter so this assertion is order-independent
        // (sibling tests increment the same counter).
        System.Threading.Interlocked.Exchange(ref StubEmbeddingProvider.InvocationCount, 0);

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IEmbeddingProvider>(new StubEmbeddingProvider());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.None },
        });

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<InMemoryRaggableStore>();

        var query = new Orkeon.Analysis.Abstractions.DTOs.Queries.SemanticQuery
        {
            Text = "hello",
            TopK = 5,
        };
        var hits = await store.SemanticSearchAsync(query, CancellationToken.None);

        Assert.Empty(hits);
        Assert.Equal(1, StubEmbeddingProvider.InvocationCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryEmbedder_caches_identical_queries()
    {
        // Arrange — reset the shared static counter before this test
        System.Threading.Interlocked.Exchange(ref StubEmbeddingProvider.InvocationCount, 0);

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IEmbeddingProvider>(new StubEmbeddingProvider());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.None },
        });

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<InMemoryRaggableStore>();

        var query = new Orkeon.Analysis.Abstractions.DTOs.Queries.SemanticQuery
        {
            Text = "hello",
            TopK = 5,
        };

        // Act — two identical queries
        await store.SemanticSearchAsync(query, CancellationToken.None);
        await store.SemanticSearchAsync(query, CancellationToken.None);

        // Assert — embedder called only once (second hit served from cache)
        Assert.Equal(1, StubEmbeddingProvider.InvocationCount);
    }

    [Fact]
    public void AddRaggableTree_leaves_queryEmbedder_null_when_no_embedding_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.None },
        });

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<InMemoryRaggableStore>();

        Assert.NotNull(store);
        Assert.Null(provider.GetService<IEmbeddingProvider>());
    }

    [Fact]
    public void AddRaggableTree_registers_inmemory_store_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();

        using var provider = services.BuildServiceProvider();
        var s1 = provider.GetRequiredService<IRaggableStore>();
        var s2 = provider.GetRequiredService<InMemoryRaggableStore>();

        Assert.Same(s1, s2);
    }

    [Fact]
    public void AddRaggableTree_resolves_builder_and_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();

        using var provider = services.BuildServiceProvider();
        var builder = provider.GetRequiredService<RaggableTreeBuilder>();
        var factory = provider.GetRequiredService<Func<RaggableTreeBuilder>>();

        Assert.NotNull(builder);
        Assert.NotNull(factory());
    }

    [Fact]
    public void AddRaggableTree_short_circuits_when_disabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree(new RaggableTreeOptions { Enabled = false });

        using var provider = services.BuildServiceProvider();
        var stored = provider.GetRequiredService<RaggableTreeOptions>();

        Assert.False(stored.Enabled);
        Assert.Null(provider.GetService<IFileSystemDiscoverer>());
        Assert.Null(provider.GetService<IRaggableStore>());
    }

    [Fact]
    public void AddRaggableTree_default_options_has_frozen_index_mode()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<RaggableTreeOptions>();

        Assert.Equal(RaggableTreeIndexMode.Frozen, options.IndexMode);
    }

    [Fact]
    public void AddRaggableTreeTools_registers_all_fifteen_tools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddRaggableTree();
        services.AddRaggableTreeTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(15, tools.Count);
        Assert.Contains(tools, t => t is CodebaseMapTool);
        Assert.Contains(tools, t => t is CodebaseSearchTool);
        Assert.Contains(tools, t => t is ComplexityReportTool);
        Assert.Contains(tools, t => t is DependencyGraphTool);
        Assert.Contains(tools, t => t is FlowTraceTool);
        Assert.Contains(tools, t => t is ImpactAnalysisTool);
        Assert.Contains(tools, t => t is PackageSummaryTool);
        Assert.Contains(tools, t => t is StatementQueryTool);
        Assert.Contains(tools, t => t is SubGraphTool);
        Assert.Contains(tools, t => t is SymbolDetailTool);
        Assert.Contains(tools, t => t is SymbolSourceTool);
        Assert.Contains(tools, t => t is IndexCodebaseTool);
        Assert.Contains(tools, t => t is IncrementalReindexTool);
        Assert.Contains(tools, t => t is IndexStatusTool);
        Assert.Contains(tools, t => t is IsPathIndexedTool);
    }

    private sealed class StubLlm : ILlmProvider
    {
        public string Name => "stub";

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Empty });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Empty });
    }

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public static int InvocationCount;

        public int Dimensions => 3;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref InvocationCount);
            IReadOnlyList<ReadOnlyMemory<float>> vectors = texts
                .Select(_ => new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]))
                .ToArray();
            return Task.FromResult(vectors);
        }
    }
}
