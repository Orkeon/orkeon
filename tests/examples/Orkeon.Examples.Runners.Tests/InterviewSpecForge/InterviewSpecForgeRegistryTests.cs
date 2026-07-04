using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using global::Orkeon.Examples.Interactive.InterviewSpecForge;
using global::Orkeon.Examples.Interactive.InterviewSpecForge.Commands;
using global::Orkeon.Examples.Interactive.InterviewSpecForge.Corpus;

namespace Orkeon.Examples.Runners.Tests.InterviewSpecForge;

public sealed class InterviewSpecForgeRegistryTests : IDisposable
{
    // CorpusSearchIndex became IDisposable (it owns a SemaphoreSlim). The registry
    // tests only inspect command wiring and never call SearchAsync, but the index
    // is stored in the returned registry's SearchCommand (which is not disposable
    // by design), so the test owns its lifetime and disposes it on teardown.
    private readonly List<CorpusSearchIndex> _searchIndexes = [];

    private static TranscriptsCatalog BuildCatalog()
    {
        // No files needed — registry tests don't enumerate the corpus.
        return new TranscriptsCatalog(
            configPath: "config.interactive.yaml",
            settingsPath: null,
            transcriptsRoot: Path.Combine(Path.GetTempPath(), "ts"),
            experimentRoot: Path.Combine(Path.GetTempPath(), "exp"),
            verbose: 0,
            llmLogEnabled: false);
    }

    private InterviewSpecForgeCommandRegistry BuildRegistry(TranscriptsCatalog catalog)
    {
        // Stub embedder is never exercised by registry tests (they only inspect
        // command wiring, never call SearchAsync), so a zero-vector embedder is
        // sufficient to satisfy CorpusSearchIndex's constructor.
        var searchIndex = new CorpusSearchIndex(new StubEmbeddingProvider(), catalog);
        _searchIndexes.Add(searchIndex);
        return new InterviewSpecForgeCommandRegistry(
            new ListCommand(catalog),
            new ShowCommand(catalog),
            new ForgeCommand(catalog, NullLogger<ForgeCommand>.Instance),
            new StatusCommand(catalog),
            new TopicsCommand(catalog),
            new TasksCommand(catalog),
            new GlossaryCommand(catalog),
            new ReplayCommand(catalog, NullLogger<ReplayCommand>.Instance),
            new SearchCommand(searchIndex),
            new TestEditCommand(catalog));
    }

    [Fact]
    public void Registry_exposes_exactly_ten_specific_commands()
    {
        var registry = BuildRegistry(BuildCatalog());

        var commands = registry.Commands.ToList();
        Assert.Equal(10, commands.Count);
    }

    [Fact]
    public void Registry_exposes_the_expected_command_names_and_aliases()
    {
        var registry = BuildRegistry(BuildCatalog());

        var byName = registry.Commands.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("list", byName.Keys);
        Assert.Contains("show", byName.Keys);
        Assert.Contains("forge", byName.Keys);
        Assert.Contains("status", byName.Keys);
        Assert.Contains("topics", byName.Keys);
        Assert.Contains("tasks", byName.Keys);
        Assert.Contains("glossary", byName.Keys);
        Assert.Contains("replay", byName.Keys);
        Assert.Contains("search", byName.Keys);

        Assert.Contains("ls", byName["list"].Aliases);
        Assert.Contains("f",  byName["forge"].Aliases);
        Assert.Contains("st", byName["status"].Aliases);
        Assert.Contains("t",  byName["topics"].Aliases);
        Assert.Contains("tk", byName["tasks"].Aliases);
        Assert.Contains("g",  byName["glossary"].Aliases);
    }

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimensions => 1;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            var vectors = new ReadOnlyMemory<float>[texts.Count];
            for (var i = 0; i < texts.Count; i++) vectors[i] = new float[] { 0f };
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
        }
    }

    [Fact]
    public void Registry_has_no_fallback_command()
    {
        var registry = BuildRegistry(BuildCatalog());

        Assert.Null(registry.Fallback);
    }

    public void Dispose()
    {
        foreach (var index in _searchIndexes)
            index.Dispose();
        GC.SuppressFinalize(this);
    }
}
