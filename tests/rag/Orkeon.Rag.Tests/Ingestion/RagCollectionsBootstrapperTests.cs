using Microsoft.Extensions.Logging;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Ingestion;

/// <summary>
/// Tests for <see cref="RagCollectionsBootstrapper"/> — the YAML <c>rag:</c> block mapped onto
/// the incremental ingestion pipeline (RAG-03/C3). Since GAP-27 its <c>sources</c> mean what they
/// say: a glob is expanded through the VFS the way <c>rag_ingest</c>, <c>orkeon rag ingest</c>,
/// <c>rag.ingest</c> and the eval harness expand it (<c>SourceGlobExpander</c>), a directory
/// ingests every file below it, a relative path resolves against the crew's folder, and a pattern
/// that matches nothing is a load warning naming the collection. They used to reach the pipeline
/// verbatim, where a glob or a directory found no loader.
/// </summary>
public class RagCollectionsBootstrapperTests
{
    private readonly FakeIngestionPipeline _pipeline = new();
    private readonly MockLogger<RagCollectionsBootstrapper> _logger = new();

    private RagCollectionsBootstrapper Bootstrapper(IFileSystemService fileSystem) =>
        new(_pipeline, fileSystem, _logger);

    private static RagCrewConfig Collection(string name, params string[] sources) => new()
    {
        Collections = new Dictionary<string, RagCollectionConfig> { [name] = new() { Sources = sources } },
    };

    private IEnumerable<string> Warnings() =>
        _logger.GetEntriesByLevel(LogLevel.Warning).Select(e => e.Message);

    [Fact]
    public async Task PrepareAsync_IngestsEveryDeclaredCollection_WithSourcesAndChunking()
    {
        var fileSystem = new FakeFileSystemService()
            .AddFile("/kb/produits/a.md", "a")
            .AddFile("/kb/produits/sub/b.md", "b")
            .AddFile("/kb/produits/notes.txt", "not markdown")
            .AddFile("/kb/faq.md", "faq")
            .AddFile("/kb/support.md", "support");
        var config = new RagCrewConfig
        {
            Collections = new Dictionary<string, RagCollectionConfig>
            {
                ["produits"] = new()
                {
                    Sources = ["/kb/produits/**/*.md", "/kb/faq.md"],
                    Chunking = new RagChunkingConfig { Strategy = "sentence", MaxTokens = 100, Overlap = 10 },
                },
                ["support"] = new() { Sources = ["/kb/support.md"] },
            },
        };

        await Bootstrapper(fileSystem).PrepareAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(2, _pipeline.Requests.Count);

        var produits = _pipeline.Requests.Single(r => r.Collection == "produits");
        Assert.Equal(["/kb/produits/a.md", "/kb/produits/sub/b.md", "/kb/faq.md"], produits.Sources.Select(s => s.Location));
        Assert.Equal("sentence", produits.ChunkingStrategy);
        Assert.Equal(400, produits.Chunking.MaxChunkSize); // max_tokens × 4 chars/token
        Assert.Equal(40, produits.Chunking.Overlap);

        var support = _pipeline.Requests.Single(r => r.Collection == "support");
        Assert.Null(support.ChunkingStrategy); // pipeline default
        Assert.Equal(["/kb/support.md"], support.Sources.Select(s => s.Location));
    }

    [Fact]
    public async Task PrepareAsync_SkipsCollectionsWithoutSources()
    {
        var config = new RagCrewConfig
        {
            Collections = new Dictionary<string, RagCollectionConfig> { ["vide"] = new() },
        };

        await Bootstrapper(new FakeFileSystemService()).PrepareAsync(config, TestContext.Current.CancellationToken);

        Assert.Empty(_pipeline.Requests);
    }

    [Fact]
    public async Task A_glob_ingests_every_file_it_matches()
    {
        var fileSystem = new FakeFileSystemService()
            .AddMount("/workspace")
            .AddFile("/workspace/docs/intro.md", "i")
            .AddFile("/workspace/docs/guide/setup.md", "s")
            .AddFile("/workspace/docs/guide/deep/faq.md", "f")
            .AddFile("/workspace/docs/logo.png", "png");

        await Bootstrapper(fileSystem).PrepareAsync(Collection("docs", "/workspace/docs/**/*.md"), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(
            ["/workspace/docs/guide/deep/faq.md", "/workspace/docs/guide/setup.md", "/workspace/docs/intro.md"],
            request.Sources.Select(s => s.Location));
    }

    [Theory]
    [InlineData("/workspace/docs/procedures")]
    [InlineData("/workspace/docs/procedures/")]
    public async Task A_directory_ingests_every_file_below_it(string source)
    {
        var fileSystem = new FakeFileSystemService()
            .AddMount("/workspace")
            .AddFile("/workspace/docs/procedures/onboarding.md", "o")
            .AddFile("/workspace/docs/procedures/refunds/policy.pdf", "%PDF")
            .AddFile("/workspace/docs/other.md", "not in it");

        await Bootstrapper(fileSystem).PrepareAsync(Collection("procedures", source), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(
            ["/workspace/docs/procedures/onboarding.md", "/workspace/docs/procedures/refunds/policy.pdf"],
            request.Sources.Select(s => s.Location));
    }

    [Fact]
    public async Task A_pattern_that_matches_nothing_is_a_warning_naming_the_collection()
    {
        var fileSystem = new FakeFileSystemService()
            .AddMount("/workspace")
            .AddFile("/workspace/docs/intro.md", "i")
            .AddDirectory("/workspace/empty");

        await Bootstrapper(fileSystem).PrepareAsync(
            Collection("docs", "/workspace/docs/**/*.pdf", "/workspace/empty", "/workspace/docs/intro.md"),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(["/workspace/docs/intro.md"], request.Sources.Select(s => s.Location));
        Assert.Contains(Warnings(), w => w.Contains("'docs'", StringComparison.Ordinal)
            && w.Contains("/workspace/docs/**/*.pdf", StringComparison.Ordinal));
        Assert.Contains(Warnings(), w => w.Contains("'docs'", StringComparison.Ordinal)
            && w.Contains("/workspace/empty", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_collection_whose_patterns_all_match_nothing_ingests_nothing()
    {
        var fileSystem = new FakeFileSystemService().AddMount("/workspace").AddDirectory("/workspace/docs");

        await Bootstrapper(fileSystem).PrepareAsync(Collection("docs", "/workspace/docs/**/*.md"), TestContext.Current.CancellationToken);

        Assert.Empty(_pipeline.Requests);
        Assert.Contains(Warnings(), w => w.Contains("'docs'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_glob_outside_every_mount_is_a_warning_not_a_failed_load()
    {
        var fileSystem = new FakeFileSystemService().AddMount("/kb").AddFile("/kb/faq.md", "faq");

        await Bootstrapper(fileSystem).PrepareAsync(
            Collection("kb", "/nowhere/**/*.md", "/kb/faq.md"), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(["/kb/faq.md"], request.Sources.Select(s => s.Location));
        Assert.Contains(Warnings(), w => w.Contains("'kb'", StringComparison.Ordinal)
            && w.Contains("/nowhere/**/*.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_relative_source_resolves_against_the_crew_directory()
    {
        var fileSystem = new FakeFileSystemService()
            .AddMount("/crew")
            .AddFile("/crew/data/faq.md", "faq")
            .AddFile("/crew/data/catalogue/a.pdf", "%PDF a")
            .AddFile("/crew/data/catalogue/old/b.pdf", "%PDF b")
            .AddFile("/crew/docs/procedures/onboarding.md", "o");
        var config = new RagCrewConfig
        {
            CrewDirectory = "/crew",
            Collections = new Dictionary<string, RagCollectionConfig>
            {
                ["produits"] = new() { Sources = ["./data/catalogue/**/*.pdf", "./data/faq.md"] },
                ["procedures"] = new() { Sources = ["docs/procedures/"] },
            },
        };

        await Bootstrapper(fileSystem).PrepareAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["/crew/data/catalogue/a.pdf", "/crew/data/catalogue/old/b.pdf", "/crew/data/faq.md"],
            _pipeline.Requests.Single(r => r.Collection == "produits").Sources.Select(s => s.Location));
        Assert.Equal(
            ["/crew/docs/procedures/onboarding.md"],
            _pipeline.Requests.Single(r => r.Collection == "procedures").Sources.Select(s => s.Location));
    }

    [Fact]
    public async Task A_relative_source_of_a_crew_with_no_folder_is_a_warning()
    {
        var fileSystem = new FakeFileSystemService().AddFile("/kb/faq.md", "faq");

        await Bootstrapper(fileSystem).PrepareAsync(
            Collection("kb", "./data/faq.md", "/kb/faq.md"), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(["/kb/faq.md"], request.Sources.Select(s => s.Location));
        Assert.Contains(Warnings(), w => w.Contains("'kb'", StringComparison.Ordinal)
            && w.Contains("./data/faq.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_web_address_passes_through_untouched_even_with_a_query_string()
    {
        var fileSystem = new FakeFileSystemService().AddMount("/kb");

        await Bootstrapper(fileSystem).PrepareAsync(
            Collection("web", "https://example.com/docs?page=2", "http://example.com/a*b"), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(["https://example.com/docs?page=2", "http://example.com/a*b"], request.Sources.Select(s => s.Location));
        Assert.Empty(Warnings());
    }

    [Fact]
    public async Task A_named_file_reaches_the_pipeline_as_written_which_reports_it_when_absent()
    {
        // A plain file path is the loaders' business, as for every other ingestion surface.
        var fileSystem = new FakeFileSystemService().AddMount("/kb");

        await Bootstrapper(fileSystem).PrepareAsync(Collection("kb", "/kb/absent.md"), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(["/kb/absent.md"], request.Sources.Select(s => s.Location));
    }

    [Fact]
    public async Task A_file_two_sources_name_is_ingested_once()
    {
        var fileSystem = new FakeFileSystemService().AddFile("/kb/faq.md", "faq").AddFile("/kb/guide.md", "g");

        await Bootstrapper(fileSystem).PrepareAsync(
            Collection("kb", "/kb/*.md", "/kb/faq.md", "/kb"), TestContext.Current.CancellationToken);

        var request = Assert.Single(_pipeline.Requests);
        Assert.Equal(["/kb/faq.md", "/kb/guide.md"], request.Sources.Select(s => s.Location));
    }
}
