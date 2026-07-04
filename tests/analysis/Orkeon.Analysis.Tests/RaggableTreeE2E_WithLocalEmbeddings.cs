using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// LE-12 — End-to-end validation of the local-embeddings stack: a project configured
/// with <see cref="EmbeddingProviderKind.LocalSmartComponents"/> must index a small
/// TypeScript codebase entirely on-device, populate 384-dim embeddings on every
/// L3 symbol, and serve semantic search queries with non-zero cosine scores.
/// </summary>
/// <remarks>
/// Marked <c>Slow</c> because the first call instantiates the BGE-micro-v2 ONNX session
/// (~200 ms boot). Every other LE test in this project is mock-only and stays fast.
/// </remarks>
[Trait("Category", "Slow")]
public class RaggableTreeE2E_WithLocalEmbeddings
{
    private const int LocalProviderDimensions = 384;

    [Fact]
    public async Task LocalProvider_indexes_typescript_project_and_serves_semantic_search()
    {
        // Arrange — a tiny 5-file TS project on disk under a VFS-mounted temp dir.
        // 5 TypeScript files (the LE-12 acceptance threshold) + a package.json manifest.
        // The discoverer only counts source files matching the language adapter, so
        // package.json doesn't add to FileCount.
        var dir = await TestFixtures.WriteDirectoryAsync(
        [
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
            ("src/utils.ts", TestFixtures.Utils),
            ("src/calls.ts", TestFixtures.Calls),
            ("src/imports.ts", TestFixtures.Imports),
            ("src/utils.spec.ts", "import { Foo } from './utils';\n"
                + "export function testFoo(): Foo { return new Foo(); }\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);

            // DI wiring — local embeddings registered BEFORE AddRaggableTree exercises the
            // short-circuit branch documented in ServiceCollectionExtensions:
            // TryAddSingleton makes the reflection-based fallback inside RaggableTree a no-op.
            var services = new ServiceCollection();
            services.AddSingleton<IFileSystemService>(fs);
            services.AddOrkeonLocalEmbeddings();
            services.AddRaggableTree(new RaggableTreeOptions
            {
                Embedding = new EmbeddingOptions
                {
                    Provider = EmbeddingProviderKind.LocalSmartComponents,
                    Local = new Orkeon.Analysis.Abstractions.DependencyInjection.LocalEmbeddingOptions
                    {
                        // Force sequential embedding for deterministic ordering in the test.
                        MaxConcurrency = 1,
                    },
                },
            });

            using var provider = services.BuildServiceProvider();

            // The embedding provider resolved by Orkeon.Analysis must be the local one.
            var embedder = provider.GetRequiredService<IEmbeddingProvider>();
            Assert.Equal(LocalProviderDimensions, embedder.Dimensions);
            Assert.Equal(
                "Orkeon.Tools.Embeddings.Local.LocalEmbeddingProvider",
                embedder.GetType().FullName);

            var builder = provider.GetRequiredService<RaggableTreeBuilder>();
            var store = provider.GetRequiredService<IRaggableStore>();

            // Act — full pipeline (discover → parse → embed locally → assemble).
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            // Push the freshly built tree into the store registered by AddRaggableTree
            // (the DI-registered store starts empty; the builder hands back a self-contained tree).
            var inMemory = (InMemoryRaggableStore)store;
            inMemory.Replace(result.Tree.Nodes, result.Tree.Edges);

            // Assert — 1) tree shape.
            Assert.True(result.Tree.Nodes.Count > 0,
                $"expected non-empty tree, got {result.Tree.Nodes.Count}");
            Assert.True(result.FileCount >= 5,
                $"expected ≥ 5 indexed TS files, got {result.FileCount}");

            // 2) Every embedded node carries a 384-dim vector — sample 20 to keep this fast.
            var embeddedSample = result.Tree.Nodes
                .Where(n => n.Embedding is not null)
                .Take(20)
                .ToList();
            Assert.NotEmpty(embeddedSample);
            Assert.All(embeddedSample, n =>
                Assert.Equal(LocalProviderDimensions, n.Embedding!.Value.Length));

            // 3) Embedding stats reflect the local provider.
            Assert.NotNull(result.EmbeddingStats);
            Assert.Equal(LocalProviderDimensions, result.EmbeddingStats!.Dimensions);
            Assert.True(result.EmbeddingStats.NodesEmbedded > 0);

            // 4) Semantic search returns ≥ 1 result with non-zero cosine score.
            var hits = await store.SemanticSearchAsync(
                new SemanticQuery { Text = "export function foo", TopK = 5 },
                CancellationToken.None);

            Assert.NotEmpty(hits);
            Assert.True(hits[0].Score > 0,
                $"expected top hit cosine > 0, got {hits[0].Score}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LocalProvider_DI_scope_does_not_register_HttpClient()
    {
        // "No network" guarantee — verify the local provider's DI scope contains no HTTP client.
        // This catches regressions where the local path accidentally pulls in IHttpClientFactory
        // through a transitive registration.
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonLocalEmbeddings();
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.LocalSmartComponents,
            },
        });

        // Inspect the registered descriptors — neither HttpClient nor IHttpClientFactory
        // should appear; the local provider runs entirely on-CPU via ONNX Runtime.
        var hasHttpClient = services.Any(d =>
            d.ServiceType.FullName == "System.Net.Http.HttpClient"
            || d.ServiceType.FullName == "System.Net.Http.IHttpClientFactory");

        Assert.False(hasHttpClient,
            "Local embeddings DI scope must not register HttpClient / IHttpClientFactory.");
    }
}
