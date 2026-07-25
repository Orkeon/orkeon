using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Knowledge;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Coverage for the RAG-03/C4 YAML surface: agent-level <c>knowledge:</c> blocks
/// (short form = list of collection names, long form = list of mappings) and the
/// crew-level <c>rag:</c> block (provider, collections with sources/chunking, defaults),
/// through the real <see cref="YamlDotNetSerializer"/> + <see cref="YamlCrewDefinitionLoader"/>
/// pipeline. Parsing only — no ingestion is triggered.
/// </summary>
public class KnowledgeAndRagYamlParsingTests
{
    private static YamlCrewDefinitionLoader BuildLoader()
        => new(
            new YamlDotNetSerializer(),
            new FakeFileSystemService(),
            NullLogger<YamlCrewDefinitionLoader>.Instance);

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
    public async Task LoadFromString_RagBlock_MapsProviderCollectionsAndDefaults()
    {
        var loader = BuildLoader();
        var yaml = """
name: rag-crew
goal: x
rag:
  provider: Sqlite
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
        Assert.Equal("Sqlite", config.Rag!.Provider);
        Assert.Equal("balanced", config.Rag.DefaultProfile);
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
  provider: "  "
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.NotNull(config.Rag);
        Assert.Null(config.Rag!.Provider);
        Assert.Empty(config.Rag.Collections);
        Assert.Null(config.Rag.DefaultProfile);
    }
}
