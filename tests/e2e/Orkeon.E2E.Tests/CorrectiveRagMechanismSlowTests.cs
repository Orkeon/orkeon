using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Onnx.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.E2E.Tests;

/// <summary>
/// RAG-06 MECHANISM demonstration on the seeded hard case <c>q-007</c> of the
/// REAL golden corpus, with REAL local BGE embeddings, the REAL hybrid document
/// store and the REAL ONNX cross-encoder for the quality baseline — and a
/// SCRIPTED chat client for exactly two corrective-graph roles:
/// <list type="number">
///   <item><description>the retrieval evaluator (grades the decoy-monopolized
///   top-5 <c>Incorrect</c>, as a real LLM grader would);</description></item>
///   <item><description>the query rewrite (returns the vocabulary bridge
///   "extend the runtime of an S-series node" — the reformulation whose
///   cross-encoder score of 0.9995 is already measured in
///   <c>examples/rag/eval/README.md</c>).</description></item>
/// </list>
/// <para><b>What this test honestly proves — and what it does not.</b> It
/// proves the corrective MECHANISM end-to-end: the graph detects the bad
/// retrieval, rewrites the probe, re-retrieves, and the citations flip from
/// "decoys only" (quality pipeline, same environment, same store) to
/// "contains <c>notes-power.md</c>". It does NOT prove that an offline run can
/// invent the vocabulary bridge: the bridge comes from a scripted LLM because
/// the CI environment has no real one — the offline heuristic rewrite cannot
/// close a pure vocabulary gap (see <c>docs/reference/limitations.md</c> and
/// the eval README). The un-scripted counterpart is the eval table's
/// <c>corrective</c> row, published as measured.</para>
/// </summary>
public sealed class CorrectiveRagMechanismSlowTests
{
    private const string HardQuestion = "How can I make the battery of my Nimbus Sense last longer?";
    private const string BridgeQuery = "extend the runtime of an S-series node";
    private const string TrulyRelevantSource = "notes-power.md";
    private const string Collection = "rag-eval-golden";

    [Fact]
    [Trait("Category", "Slow")]
    public async Task CorrectiveGraph_RecoversTheSeededCase_ThatQualityMisses()
    {
        var ct = TestContext.Current.CancellationToken;

        var evalSource = Path.Combine(FindRepositoryRoot(), "examples", "rag", "eval");
        var workspace = Directory.CreateTempSubdirectory("orkeon-rag-crag-").FullName;
        try
        {
            CopyDirectory(evalSource, workspace);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Orkeon:Rag:Ingestion:ManifestDirectory"] = "/workspace/.state/manifests",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
                new DiskBackedFileSystemService(workspace, "/workspace"));

            // Real on-device BGE-micro-v2 embeddings — the exact environment in
            // which q-007 is proven to fail plain retrieval.
            services.AddOrkeonLocalEmbeddings();

            // The QUALITY baseline generates through the deterministic extractive
            // stub (citations are what matters here, not prose).
            using var offlineChatClient = new Orkeon.Rag.Evaluation.ExtractiveOfflineChatClient();
            services.AddSingleton<IChatClient>(offlineChatClient);

            services.AddOrkeonInfrastructure();
            services.AddOrkeonRag(configuration);
            services.AddOrkeonOnnxReranker(); // quality needs the real cross-encoder

            await using var provider = services.BuildServiceProvider();

            // Ingest the REAL golden corpus (12 markdown documents).
            var corpusFiles = Directory.EnumerateFiles(Path.Combine(workspace, "corpus"), "*.md")
                .Select(path => new SourceDescriptor { Location = "/workspace/corpus/" + Path.GetFileName(path) })
                .OrderBy(s => s.Location, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(12, corpusFiles.Count);

            var ingestion = await provider.GetRequiredService<IIngestionPipeline>().IngestAsync(
                new IngestionRequest { Collection = Collection, Sources = [.. corpusFiles] },
                ct);
            Assert.Empty(ingestion.Errors);

            var query = new RagQuery { Text = HardQuestion, Collection = Collection, TopN = 5 };

            // ── baseline: the QUALITY pipeline misses notes-power.md ─────────
            var quality = provider.GetRequiredService<IRagProfileResolver>().Resolve("quality");
            var qualityAnswer = await quality.QueryAsync(query, ct);

            TestContext.Current.TestOutputHelper?.WriteLine(
                "quality citations: " + string.Join(", ", qualityAnswer.Citations.Select(c => c.SourceId)));

            Assert.NotEmpty(qualityAnswer.Citations);
            Assert.DoesNotContain(qualityAnswer.Citations,
                c => c.SourceId.Contains(TrulyRelevantSource, StringComparison.Ordinal));

            // ── corrective graph over the SAME store/embeddings ──────────────
            using var scriptedChat = new ScriptedCorrectiveChatClient();
            var corrective = new CorrectiveRagPipeline(
                provider.GetRequiredService<IDocumentStore>(),
                provider.GetRequiredService<IEmbeddingProvider>(),
                scriptedChat,
                new LlmRetrievalEvaluator(scriptedChat),
                RagProfilePresets.Create(RagProfile.Corrective));
                // No CorrectiveRagPipelineDependencies — the mechanism under test is the
                // retrieve → evaluate → rewrite loop; the graph traces the
                // check_groundedness node as skipped.

            var correctiveAnswer = await corrective.QueryAsync(query, ct);

            TestContext.Current.TestOutputHelper?.WriteLine(
                "corrective citations: " + string.Join(", ", correctiveAnswer.Citations.Select(c => c.SourceId)));
            TestContext.Current.TestOutputHelper?.WriteLine(
                "corrective steps: " + string.Join(" → ", correctiveAnswer.Trace.Steps.Select(s => s.Name)));

            // The corrective loop actually looped: evaluate graded Incorrect,
            // rewrite produced the bridge probe, retrieve ran again.
            Assert.True(correctiveAnswer.Trace.Iterations >= 1);
            Assert.Contains(BridgeQuery, correctiveAnswer.Trace.QueryVariants);
            Assert.Contains(correctiveAnswer.Trace.Steps, s => s.Name == "corrective:rewrite_query");
            Assert.Contains(correctiveAnswer.Trace.Verdicts, v => v.Grade == RetrievalGrade.Incorrect);

            // THE flip: the citations now surface the truly relevant document
            // that the quality pipeline (same environment) missed — at rank 1,
            // as the bridge query is lexically aligned with notes-power.md.
            Assert.Contains(correctiveAnswer.Citations,
                c => c.SourceId.Contains(TrulyRelevantSource, StringComparison.Ordinal));
            Assert.NotEmpty(correctiveAnswer.Citations);
            Assert.Contains(TrulyRelevantSource, correctiveAnswer.Citations[0].SourceId, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { /* scratch dir — best effort */ }
        }
    }

    /// <summary>
    /// The scripted chat client of the mechanism test — labelled as such. It
    /// stands in for a real LLM in exactly the two corrective roles the CI
    /// environment cannot exercise (grading and vocabulary-bridging rewrite);
    /// generation falls through to a fixed grounded sentence because the
    /// assertions bear on citations, not prose.
    /// </summary>
    private sealed class ScriptedCorrectiveChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            var system = list.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
            var user = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

            string text;
            if (string.Equals(system, LlmRetrievalEvaluator.SystemPrompt, StringComparison.Ordinal))
            {
                // Grader role: Correct only when the truly relevant passage
                // (eco mode / S-series runtime) is in the retrieved set —
                // the decoys repeat the question's vocabulary, not the answer.
                text = user.Contains("eco mode", StringComparison.OrdinalIgnoreCase)
                    ? """{"grade": "correct", "reason": "the passages explain how to stretch the node runtime"}"""
                    : """{"grade": "incorrect", "reason": "the passages reuse the question's words (battery, Nimbus Sense) but none explains how to extend the runtime"}""";
            }
            else if (string.Equals(system, CorrectiveRagPipeline.RewriteSystemPrompt, StringComparison.Ordinal))
            {
                // Rewrite role: the vocabulary bridge (measured at 0.9995 by the
                // cross-encoder in the eval README).
                text = BridgeQuery;
            }
            else
            {
                text = "Switch the node to eco mode to stretch its runtime [1].";
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
            // Nothing to dispose.
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Orkeon.sln not found above the test base directory.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
