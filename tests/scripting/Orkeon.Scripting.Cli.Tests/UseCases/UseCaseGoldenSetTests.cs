using System.Diagnostics;
using System.Globalization;
using System.Text;
using Orkeon.Scripting.Cli.Commands.UseCases;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// The golden set of <c>orkeon usecases search</c> (STUDIO-38 D-08), run against the embedded
/// catalogue in both modes — BM25 alone, and hybrid with the real local model — and reported as
/// recall@k per language and per mode. There is no threshold yet: the report is the product,
/// the measure that decides, language by language, whether meaning is fused in (D-02). A floor
/// comes once the sheets carry their texts (STUDIO-37) and a first real measurement exists.
/// <para>
/// Loads the ONNX Runtime native library, like <c>Orkeon.Tools.Embeddings.Local.Tests</c>.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public sealed class UseCaseGoldenSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_golden_set_runs_in_both_modes_and_reports_recall_per_language()
    {
        var golden = LoadGoldenSet();
        var catalog = UseCaseCatalog.Embedded;

        // Every expected id must exist: a golden case pointing at nothing measures nothing.
        Assert.All(golden.Cases.SelectMany(c => c.Expected), id => Assert.True(catalog.TryGet(id, out _), id));

        using var terms = new UseCaseSearchEngine(catalog, UseCaseSearchPolicy.Everywhere(UseCaseSearchMode.Bm25), UseCaseLocalModel.Load);
        using var hybrid = new UseCaseSearchEngine(catalog, UseCaseSearchPolicy.Everywhere(UseCaseSearchMode.Hybrid), UseCaseLocalModel.Load);
        using var shipped = new UseCaseSearchEngine(catalog, UseCaseSearchPolicy.Default, UseCaseLocalModel.Load);

        var coldStart = Stopwatch.StartNew();
        var first = await hybrid.SearchAsync(new UseCaseQuery { Text = "warm-up", Language = "en" }, Ct);
        coldStart.Stop();

        // The native runtime loaded and the English side is live: otherwise "hybrid" below
        // would silently measure BM25 twice.
        Assert.Equal(UseCaseSearchMode.Hybrid, first.Mode);
        Assert.Null(first.Degraded);

        var runs = new List<GoldenRun>();
        foreach (var goldenCase in golden.Cases)
        {
            runs.Add(await RunAsync(terms, "bm25", goldenCase, golden.K));
            runs.Add(await RunAsync(hybrid, "hybrid", goldenCase, golden.K));
            runs.Add(await RunAsync(shipped, "shipped", goldenCase, golden.K));
        }

        var detected = golden.Cases.Count(c => UseCaseLanguages.Detect(c.Query) == c.Lang);
        var report = Report(golden, runs, detected, coldStart.Elapsed);
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "usecases-golden-report.md"), report, Ct);

        foreach (var language in UseCaseLanguages.All)
        {
            Assert.Contains(golden.Cases, c => c.Lang == language);
            Assert.Contains($"| {language} |", report, StringComparison.Ordinal);
        }

        Assert.All(runs.Where(r => r.Mode == "hybrid"), run => Assert.Equal(UseCaseSearchMode.Hybrid, run.Answer.Mode));
        Assert.All(runs.Where(r => r.Mode == "bm25"), run => Assert.Equal(UseCaseSearchMode.Bm25, run.Answer.Mode));
    }

    /// <summary>
    /// D-04: the model comes from a minimal container — no settings, no Llm section, no
    /// RaggableTree — and still resolves, which it only does with IFileSystemService registered.
    /// </summary>
    [Fact]
    public async Task The_local_model_loads_in_a_minimal_container()
    {
        var model = UseCaseLocalModel.Load();
        try
        {
            var vectors = await model.EmbedBatchAsync(["daily email digest"], Ct);

            Assert.Equal(384, model.Dimensions);
            Assert.Equal(384, vectors[0].Length);
        }
        finally
        {
            (model as IDisposable)?.Dispose();
        }
    }

    private static async Task<GoldenRun> RunAsync(UseCaseSearchEngine engine, string mode, GoldenCase goldenCase, int k)
    {
        var watch = Stopwatch.StartNew();
        var answer = await engine.SearchAsync(new UseCaseQuery { Text = goldenCase.Query, Language = goldenCase.Lang, Top = k }, Ct);
        watch.Stop();

        var found = answer.Matches.Select(m => m.UseCase.Id).ToList();
        var recall = goldenCase.Expected.Count(found.Contains) / (double)goldenCase.Expected.Count;
        var firstHit = found.FindIndex(goldenCase.Expected.Contains);
        var reciprocalRank = firstHit < 0 ? 0 : 1.0 / (firstHit + 1);
        return new GoldenRun(goldenCase, mode, answer, recall, reciprocalRank, watch.Elapsed);
    }

    private static string Report(GoldenSet golden, List<GoldenRun> runs, int detected, TimeSpan coldStart)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Golden set `{golden.Name}` — recall@{golden.K} per language and mode");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| Language | Cases | BM25 recall@{golden.K} | Hybrid recall@{golden.K} | Shipped recall@{golden.K} | BM25 MRR | Hybrid MRR |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var language in UseCaseLanguages.All.Append("all"))
        {
            var cases = runs.Where(r => language == "all" || r.Case.Lang == language).ToList();
            var count = cases.Select(r => r.Case.Id).Distinct(StringComparer.Ordinal).Count();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {language} | {count} | {Mean(cases, "bm25", r => r.Recall)} | {Mean(cases, "hybrid", r => r.Recall)} "
                + $"| {Mean(cases, "shipped", r => r.Recall)} | {Mean(cases, "bm25", r => r.ReciprocalRank)} | {Mean(cases, "hybrid", r => r.ReciprocalRank)} |");
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Shipped policy: {string.Join(", ", UseCaseLanguages.All.Select(l => $"{l}={Spell(UseCaseSearchPolicy.Default.ModeFor(l))}"))}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Language detection (no --lang): {detected}/{golden.Cases.Count} cases detected right.");
        text.AppendLine(CultureInfo.InvariantCulture, $"First hybrid search (model load + {UseCaseCatalog.Embedded.UseCases.Count} English sheets embedded): {coldStart.TotalMilliseconds:F0} ms.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Per query, warm: BM25 {Latency(runs, "bm25")} · hybrid {Latency(runs, "hybrid")}.");
        text.AppendLine();
        text.AppendLine("| Case | Lang | Expected | BM25 top 5 | Hybrid top 5 |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var goldenCase in golden.Cases)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {goldenCase.Id} | {goldenCase.Lang} | {string.Join(", ", goldenCase.Expected)} | {Top(runs, goldenCase, "bm25")} | {Top(runs, goldenCase, "hybrid")} |");
        }

        return text.ToString();
    }

    private static string Mean(List<GoldenRun> runs, string mode, Func<GoldenRun, double> metric)
    {
        var selected = runs.Where(r => r.Mode == mode).ToList();
        return selected.Count == 0 ? "—" : selected.Average(metric).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string Latency(List<GoldenRun> runs, string mode)
    {
        var selected = runs.Where(r => r.Mode == mode).Select(r => r.Elapsed.TotalMilliseconds).Order().ToList();
        var median = selected[selected.Count / 2];
        return string.Create(CultureInfo.InvariantCulture, $"median {median:F1} ms, max {selected[^1]:F1} ms");
    }

    private static string Top(List<GoldenRun> runs, GoldenCase goldenCase, string mode)
    {
        var run = runs.Single(r => r.Case.Id == goldenCase.Id && r.Mode == mode);
        return run.Answer.Matches.Count == 0
            ? "(none)"
            : string.Join(" ", run.Answer.Matches.Select(m => goldenCase.Expected.Contains(m.UseCase.Id) ? $"**{m.UseCase.Id}**" : m.UseCase.Id));
    }

    private static string Spell(UseCaseSearchMode mode) => mode == UseCaseSearchMode.Hybrid ? "hybrid" : "bm25";

    private static GoldenSet LoadGoldenSet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        var yaml = File.ReadAllText(Path.Combine(dir!.FullName, "examples", "usecases.golden.yaml"));

        var set = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build()
            .Deserialize<GoldenSet>(yaml);

        Assert.NotEmpty(set.Cases);
        return set;
    }

    private sealed record GoldenRun(
        GoldenCase Case, string Mode, UseCaseAnswer Answer, double Recall, double ReciprocalRank, TimeSpan Elapsed);

    /// <summary>The YAML shape of <c>examples/usecases.golden.yaml</c>.</summary>
    public sealed class GoldenSet
    {
        public string Name { get; set; } = string.Empty;
        public int K { get; set; } = 5;
        public List<GoldenCase> Cases { get; set; } = [];
    }

    /// <summary>One golden query and the ids its first <c>k</c> results must hold.</summary>
    public sealed class GoldenCase
    {
        public string Id { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public List<string> Expected { get; set; } = [];
    }
}
