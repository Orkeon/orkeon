using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Evaluation;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.E2E.Tests;

/// <summary>
/// RAG-04/C1 acceptance on the REAL golden dataset with REAL local BGE
/// embeddings (zero network, zero API key — generation is the deterministic
/// extractive stub). Two things are proven at once:
/// <list type="number">
///   <item><description>
///     the harness floor: aggregate recall@5 and MRR over the regular cases
///     (tag <c>correctif</c> excluded) meet the CI thresholds;
///   </description></item>
///   <item><description>
///     the seeded hard case <c>q-007</c> FAILS plain vector retrieval — the
///     assertion is deliberately INVERTED (recall@5 == 0): the case was
///     built so that five decoy documents monopolize the question's
///     vocabulary ("battery", "last longer", "Nimbus Sense") while the truly
///     relevant <c>notes-power.md</c> avoids it. The corrective engine
///     (RAG-06) is the batch expected to flip this assertion.
///   </description></item>
/// </list>
/// </summary>
public sealed class RagEvalGoldenDatasetSlowTests
{
    /// <summary>Corrective-case id in the golden dataset.</summary>
    private const string CorrectiveCaseId = "q-007";

    /// <summary>CI gate floors — keep in sync with .github/workflows/rag-eval.yml.</summary>
    private const double MinGatedRecallAt5 = 0.80;
    private const double MinGatedMrr = 0.70;

    [Fact]
    [Trait("Category", "Slow")]
    public async Task GoldenDataset_WithLocalBge_MeetsFloors_AndCorrectiveCaseFailsPlainRetrieval()
    {
        var ct = TestContext.Current.CancellationToken;

        // The REAL versioned dataset + corpus, copied to a scratch workspace so
        // manifests and reports never land in the repository.
        var evalSource = Path.Combine(FindRepositoryRoot(), "examples", "rag", "eval");
        var workspace = Directory.CreateTempSubdirectory("orkeon-rag-eval-").FullName;
        try
        {
            CopyDirectory(evalSource, workspace);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Keep incremental-ingestion state inside the scratch mount.
                    ["Orkeon:Rag:Ingestion:ManifestDirectory"] = "/workspace/.state/manifests",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
                new DiskBackedFileSystemService(workspace, "/workspace"));

            // Real on-device BGE-micro-v2 embeddings (the infra default resolver
            // adapts the Analysis-side provider to the Application port).
            services.AddOrkeonLocalEmbeddings();

            // Deterministic extractive generation — the heuristic judge's
            // expected_substrings then measure retrieval, not an LLM.
            using var offlineChatClient = new ExtractiveOfflineChatClient();
            services.AddSingleton<IChatClient>(offlineChatClient);

            services.AddOrkeonInfrastructure();
            services.AddOrkeonRag(configuration);

            await using var provider = services.BuildServiceProvider();
            var harness = provider.GetRequiredService<IRagEvalHarness>();

            var result = await harness.RunAsync(
                new RagEvalRunRequest
                {
                    DatasetPath = "/workspace/golden.yaml",
                    OutputDirectory = "/workspace/.out/rag/eval",
                },
                ct);

            // Corpus really ingested (10 markdown documents).
            Assert.NotNull(result.Ingestion);
            Assert.Equal(10, result.Ingestion!.DocumentsLoaded);
            Assert.Empty(result.Ingestion.Errors);

            var report = Assert.Single(result.Reports);

            // Offline run: the judge mode is labelled heuristic — never ambiguous.
            Assert.Equal(RagJudgeMode.Heuristic, report.Judge);
            Assert.All(report.Cases, c => Assert.Equal(RagJudgeMode.Heuristic, c.Judge));

            // CI floors on the regular cases (correctif excluded).
            var gatedRecall = RagEvalGate.MeanRecallExcluding(report, RagEvalCase.CorrectiveTag);
            var gatedMrr = RagEvalGate.MeanReciprocalRankExcluding(report, RagEvalCase.CorrectiveTag);
            Assert.NotNull(gatedRecall);
            Assert.NotNull(gatedMrr);
            Assert.True(gatedRecall >= MinGatedRecallAt5,
                $"Aggregate recall@5 (correctif excluded) regressed: {gatedRecall:F3} < {MinGatedRecallAt5:F2}");
            Assert.True(gatedMrr >= MinGatedMrr,
                $"Aggregate MRR (correctif excluded) regressed: {gatedMrr:F3} < {MinGatedMrr:F2}");

            // INVERTED assertion (wanted failure): the seeded hard case must NOT be
            // recovered by plain vector retrieval — that is exactly what RAG-06's
            // corrective engine will have to fix. If this starts passing, the decoys
            // lost their teeth: rework the corpus, do not delete the assertion.
            var corrective = Assert.Single(report.Cases, c => c.CaseId == CorrectiveCaseId);
            Assert.Contains(RagEvalCase.CorrectiveTag, corrective.Tags);
            Assert.Equal(0.0, corrective.RecallAtK);

            // Reports written through the VFS.
            Assert.Equal(2, result.WrittenFiles.Count);
            Assert.True(File.Exists(Path.Combine(workspace, ".out", "rag", "eval", "golden-default.md")));
            Assert.True(File.Exists(Path.Combine(workspace, ".out", "rag", "eval", "golden-default.json")));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { /* scratch dir — best effort */ }
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
