using Orkeon.Domain.Knowledge;

namespace Orkeon.Domain.Tests.Knowledge;

/// <summary>
/// Tests for the <see cref="KnowledgeAttachment"/> value object (RAG-03/C4):
/// defaults, factory validation, and invariants.
/// </summary>
public class KnowledgeAttachmentTests
{
    [Fact]
    public void Create_WithCollectionOnly_UsesDefaults()
    {
        var attachment = KnowledgeAttachment.Create("produits");

        Assert.Equal("produits", attachment.Collection);
        Assert.Equal(KnowledgeAttachment.DefaultTopK, attachment.TopK);
        Assert.Null(attachment.MinScore);
        Assert.Null(attachment.Profile);
        Assert.Null(attachment.MaxContextTokens);
    }

    [Fact]
    public void Create_WithAllOptions_SetsAllProperties()
    {
        var attachment = KnowledgeAttachment.Create(
            "procedures", topK: 8, minScore: 0.35, profile: "quality", maxContextTokens: 1500);

        Assert.Equal("procedures", attachment.Collection);
        Assert.Equal(8, attachment.TopK);
        Assert.Equal(0.35, attachment.MinScore);
        Assert.Equal("quality", attachment.Profile);
        Assert.Equal(1500, attachment.MaxContextTokens);
    }

    [Fact]
    public void Create_TrimsCollectionAndProfile()
    {
        var attachment = KnowledgeAttachment.Create("  produits  ", profile: "  fast  ");

        Assert.Equal("produits", attachment.Collection);
        Assert.Equal("fast", attachment.Profile);
    }

    [Fact]
    public void Create_WithBlankProfile_NormalizesToNull()
    {
        var attachment = KnowledgeAttachment.Create("produits", profile: "   ");

        Assert.Null(attachment.Profile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyCollection_Throws(string collection)
    {
        var ex = Assert.Throws<ArgumentException>(() => KnowledgeAttachment.Create(collection));
        Assert.Contains("collection", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithNullCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeAttachment.Create(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveTopK_Throws(int topK)
    {
        var ex = Assert.Throws<ArgumentException>(() => KnowledgeAttachment.Create("produits", topK: topK));
        Assert.Contains("TopK", ex.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_WithMinScoreOutOfRange_Throws(double minScore)
    {
        var ex = Assert.Throws<ArgumentException>(() => KnowledgeAttachment.Create("produits", minScore: minScore));
        Assert.Contains("MinScore", ex.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Create_WithMinScoreAtBounds_Succeeds(double minScore)
    {
        var attachment = KnowledgeAttachment.Create("produits", minScore: minScore);
        Assert.Equal(minScore, attachment.MinScore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Create_WithNonPositiveMaxContextTokens_Throws(int maxContextTokens)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => KnowledgeAttachment.Create("produits", maxContextTokens: maxContextTokens));
        Assert.Contains("MaxContextTokens", ex.Message);
    }

    [Fact]
    public void Validate_OnRecordBuiltWithoutFactory_EnforcesInvariants()
    {
        var attachment = new KnowledgeAttachment { Collection = "produits", TopK = 0 };

        Assert.Throws<ArgumentException>(() => attachment.Validate());
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var a = KnowledgeAttachment.Create("produits", topK: 8, minScore: 0.5);
        var b = KnowledgeAttachment.Create("produits", topK: 8, minScore: 0.5);

        Assert.Equal(a, b);
    }
}
