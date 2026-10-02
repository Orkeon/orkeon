using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Knowledge;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Coverage for the RAG-03/C4 YAML surface: agent-level <c>knowledge:</c> blocks
/// (short form = list of collection names, long form = list of mappings) and the
/// crew-level <c>rag:</c> block (collections with sources/chunking, defaults; the retired
/// <c>provider</c> key draws a warning),
/// through the real <see cref="YamlDotNetSerializer"/> + <see cref="YamlCrewDefinitionLoader"/>
/// pipeline. Parsing only — no ingestion is triggered.
/// </summary>
public class KnowledgeAndRagYamlParsingTests
{
    private static YamlCrewDefinitionLoader BuildLoader(
        Microsoft.Extensions.Logging.ILogger<YamlCrewDefinitionLoader>? logger = null)
        => new(
            new YamlDotNetSerializer(),
            new FakeFileSystemService(),
            logger ?? NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task LoadFromString_ShortForm_AttachesCollectionsWithDefaults()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
agents:
  support:
    role: Support agent
    goal: Answer questions
    knowledge: [produits, procedures]
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Equal(2, agent.KnowledgeAttachments.Count);
        Assert.Equal(["produits", "procedures"],
            agent.KnowledgeAttachments.Select(a => a.Collection).ToArray());
        Assert.All(agent.KnowledgeAttachments, a =>
        {
            Assert.Equal(KnowledgeAttachment.DefaultTopK, a.TopK);
            Assert.Null(a.MinScore);
            Assert.Null(a.Profile);
            Assert.Null(a.MaxContextTokens);
        });
    }

    [Fact]
    public async Task LoadFromString_LongForm_SnakeCase_MapsAllFields()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
agents:
  expert:
    role: Expert
    goal: Deep answers
    knowledge:
      - collection: procedures
        profile: quality
        top_k: 8
        min_score: 0.35
        max_context_tokens: 1500
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        var attachment = Assert.Single(agent.KnowledgeAttachments);
        Assert.Equal("procedures", attachment.Collection);
        Assert.Equal(8, attachment.TopK);
        Assert.Equal(0.35, attachment.MinScore);
        Assert.Equal("quality", attachment.Profile);
        Assert.Equal(1500, attachment.MaxContextTokens);
    }

    [Fact]
    public async Task LoadFromString_LongForm_CamelCase_MapsAllFields()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
agents:
  expert:
    role: Expert
    goal: Deep answers
    knowledge:
      - collection: produits
        topK: 3
        minScore: 0.5
        maxContextTokens: 800
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var attachment = Assert.Single(Assert.Single(config.Agents).KnowledgeAttachments);
        Assert.Equal("produits", attachment.Collection);
        Assert.Equal(3, attachment.TopK);
        Assert.Equal(0.5, attachment.MinScore);
        Assert.Equal(800, attachment.MaxContextTokens);
    }

    [Fact]
    public async Task LoadFromString_MixedShortAndLongForms_PreservesOrderAndDefaults()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
agents:
  support:
    role: Support
    goal: Answer
    knowledge:
      - produits
      - collection: procedures
        top_k: 4
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Equal(2, agent.KnowledgeAttachments.Count);
        Assert.Equal("produits", agent.KnowledgeAttachments[0].Collection);
        Assert.Equal(KnowledgeAttachment.DefaultTopK, agent.KnowledgeAttachments[0].TopK);
        Assert.Equal("procedures", agent.KnowledgeAttachments[1].Collection);
        Assert.Equal(4, agent.KnowledgeAttachments[1].TopK);
    }

    [Fact]
    public async Task LoadFromString_MalformedKnowledgeEntries_AreSkippedNotFatal()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
agents:
  support:
    role: Support
    goal: Answer
    knowledge:
      - top_k: 8
      - collection: produits
        top_k: not-a-number
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        // Entry without 'collection' is skipped; the non-numeric top_k falls back to the default.
        var attachment = Assert.Single(agent.KnowledgeAttachments);
        Assert.Equal("produits", attachment.Collection);
        Assert.Equal(KnowledgeAttachment.DefaultTopK, attachment.TopK);
    }

    [Fact]
    public async Task LoadFromString_RagBlock_MapsCollectionsAndDefaults()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
rag:
  collections:
    produits:
      sources: ["./data/catalogue/**/*.pdf", "./data/faq.md"]
      chunking: { strategy: recursive, max_tokens: 512, overlap: 64 }
    procedures:
      sources: ["./docs/procedures/"]
  defaults: { profile: balanced }
agents:
  support:
    role: Support
    goal: Answer
    knowledge: [produits]
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.NotNull(config.Rag);
        Assert.Equal("balanced", config.Rag!.DefaultProfile);
        Assert.Equal(2, config.Rag.Collections.Count);

        var produits = config.Rag.Collections["produits"];
        Assert.Equal(["./data/catalogue/**/*.pdf", "./data/faq.md"], produits.Sources);
        Assert.NotNull(produits.Chunking);
        Assert.Equal("recursive", produits.Chunking!.Strategy);
        Assert.Equal(512, produits.Chunking.MaxTokens);
        Assert.Equal(64, produits.Chunking.Overlap);

        var procedures = config.Rag.Collections["procedures"];
        Assert.Equal(["./docs/procedures/"], procedures.Sources);
        Assert.Null(procedures.Chunking);

        // Read from a string: no folder for a relative source to resolve against.
        Assert.Null(config.Rag.CrewDirectory);
    }

    private const string RelativeRagCrew = """
name: rag-crew
goal: x
rag:
  collections:
    procedures:
      sources: ["./docs/procedures/"]
agents:
  support: { role: Support, goal: Answer, knowledge: [procedures] }
tasks:
  answer: { description: Answer, expectedOutput: An answer, agent: support }
""";

    [Fact]
    public async Task LoadFromFile_RagBlock_RecordsTheFolderOfTheCrewFile()
    {
        // GAP-27: a relative source resolves against the crew's folder — /crew under
        // `orkeon run crew.yaml` — so the loader, the one that knows where the crew lives,
        // records it. The sources themselves stay as written.
        var fileSystem = new FakeFileSystemService().AddMount("/crew").AddFile("/crew/crew.yaml", RelativeRagCrew);
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), fileSystem, NullLogger<YamlCrewDefinitionLoader>.Instance);

        var config = await loader.LoadFromFileAsync("/crew/crew.yaml", TestContext.Current.CancellationToken);

        Assert.Equal("/crew", config.Rag!.CrewDirectory);
        Assert.Equal(["./docs/procedures/"], config.Rag.Collections["procedures"].Sources);
    }

    [Fact]
    public async Task LoadFromDirectory_RagBlock_RecordsTheCrewDirectory()
    {
        var settings = """
name: rag-crew
goal: x
rag:
  collections:
    procedures:
      sources: ["./docs/procedures/"]
""";
        var fileSystem = new FakeFileSystemService()
            .AddMount("/crews-1")
            .AddFile("/crews-1/config.yaml", settings)
            .AddFile("/crews-1/agents/support.yaml", "role: Support\ngoal: Answer\nknowledge: [procedures]\n")
            .AddFile("/crews-1/tasks/answer.yaml", "description: Answer\nexpectedOutput: An answer\nagent: support\n");
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), fileSystem, NullLogger<YamlCrewDefinitionLoader>.Instance);

        var config = await loader.LoadFromDirectoryAsync("/crews-1", TestContext.Current.CancellationToken);

        Assert.Equal("/crews-1", config.Rag!.CrewDirectory);
    }

    [Fact]
    public async Task LoadFromString_RagChunkingPartial_FallsBackToDefaults()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
rag:
  collections:
    docs:
      sources: ["./docs/"]
      chunking: { max_tokens: 256 }
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var chunking = config.Rag!.Collections["docs"].Chunking;
        Assert.NotNull(chunking);
        Assert.Equal("recursive", chunking!.Strategy);
        Assert.Equal(256, chunking.MaxTokens);
        Assert.Equal(64, chunking.Overlap);
    }

    [Fact]
    public async Task LoadFromString_WithoutKnowledgeOrRag_BehaviorUnchanged()
    {
        // Non-regression: a crew that declares neither block parses exactly as before.
        var loader = BuildLoader();
        var yaml = """
name: plain-crew
goal: x
agents:
  worker:
    role: Worker
    goal: Work
tasks:
  work:
    description: Do work
    expectedOutput: Done
    agent: worker
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Null(config.Rag);
        var agent = Assert.Single(config.Agents);
        Assert.Empty(agent.KnowledgeAttachments);
        Assert.Single(config.Tasks);
        Assert.Equal("plain-crew", config.Name);
    }

    [Fact]
    public async Task LoadFromString_EmptyRagBlockFields_NormalizeToNullAndEmpty()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
rag:
  defaults: { profile: "  " }
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.NotNull(config.Rag);
        Assert.Empty(config.Rag!.Collections);
        Assert.Null(config.Rag.DefaultProfile);
    }

    [Fact]
    public async Task LoadFromString_RetiredRagProviderKey_DrawsAWarningAndIsIgnored()
    {
        // GAP-02: rag.provider was parsed and read by nobody — the store is the host's
        // choice (Orkeon:Rag:Provider). The key is gone; a crew that still writes it is
        // told so instead of being ignored in silence.
        var logger = new Orkeon.Tests.Shared.Doubles.MockLogger<YamlCrewDefinitionLoader>();
        var loader = BuildLoader(logger);
        var yaml = """
name: rag-crew
goal: x
rag:
  provider: Sqlite
  collections:
    docs: { sources: ["./docs/"] }
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.True(config.Rag!.Collections.ContainsKey("docs"));
        var warning = Assert.Single(logger.LogEntries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("rag.provider", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Rag:Provider", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromString_WithoutRetiredKeys_DoesNotWarn()
    {
        var logger = new Orkeon.Tests.Shared.Doubles.MockLogger<YamlCrewDefinitionLoader>();
        var loader = BuildLoader(logger);
        var yaml = """
name: rag-crew
goal: x
rag:
  collections:
    docs: { sources: ["./docs/"] }
agents:
  support: { role: Support, goal: Answer, knowledge: [docs] }
""";

        await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.LogEntries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }
}
