using Orkeon.Domain.Agent;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Domain.Tests.Builders;

/// <summary>
/// Tests for <see cref="AgentBuilder.WithKnowledge(string)"/> and its overloads (RAG-03/C4):
/// cumulative attachments, option configuration, validation, and default emptiness.
/// </summary>
public class AgentBuilderKnowledgeTests
{
    private static AgentBuilder MinimalAgent() =>
        new AgentBuilder().Role("Support Agent").Goal("Answer product questions");

    [Fact]
    public void Build_WithoutKnowledge_AgentHasEmptyAttachments()
    {
        var agent = MinimalAgent().Build();

        Assert.Empty(agent.KnowledgeAttachments);
    }

    [Fact]
    public void WithKnowledge_ShortForm_AttachesCollectionWithDefaults()
    {
        var agent = MinimalAgent().WithKnowledge("produits").Build();

        var attachment = Assert.Single(agent.KnowledgeAttachments);
        Assert.Equal("produits", attachment.Collection);
        Assert.Equal(KnowledgeAttachment.DefaultTopK, attachment.TopK);
        Assert.Null(attachment.MinScore);
        Assert.Null(attachment.Profile);
    }

    [Fact]
    public void WithKnowledge_WithOptions_AppliesConfiguredValues()
    {
        var agent = MinimalAgent()
            .WithKnowledge("produits", opts =>
            {
                opts.TopK = 8;
                opts.MinScore = 0.35;
                opts.Profile = "quality";
                opts.MaxContextTokens = 1200;
            })
            .Build();

        var attachment = Assert.Single(agent.KnowledgeAttachments);
        Assert.Equal("produits", attachment.Collection);
        Assert.Equal(8, attachment.TopK);
        Assert.Equal(0.35, attachment.MinScore);
        Assert.Equal("quality", attachment.Profile);
        Assert.Equal(1200, attachment.MaxContextTokens);
    }

    [Fact]
    public void WithKnowledge_IsCumulative_AcrossAllOverloads()
    {
        var agent = MinimalAgent()
            .WithKnowledge("produits")
            .WithKnowledge("procedures", opts => opts.TopK = 3)
            .WithKnowledge(KnowledgeAttachment.Create("faq", minScore: 0.5))
            .Build();

        Assert.Equal(3, agent.KnowledgeAttachments.Count);
        Assert.Equal(["produits", "procedures", "faq"],
            agent.KnowledgeAttachments.Select(a => a.Collection).ToArray());
        Assert.Equal(3, agent.KnowledgeAttachments[1].TopK);
        Assert.Equal(0.5, agent.KnowledgeAttachments[2].MinScore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithKnowledge_EmptyCollection_Throws(string collection)
    {
        var builder = MinimalAgent();

        Assert.Throws<ArgumentException>(() => builder.WithKnowledge(collection));
    }

    [Fact]
    public void WithKnowledge_InvalidOptions_Throws()
    {
        var builder = MinimalAgent();

        Assert.Throws<ArgumentException>(
            () => builder.WithKnowledge("produits", opts => opts.TopK = 0));
    }

    [Fact]
    public void WithKnowledge_NullConfigure_Throws()
    {
        var builder = MinimalAgent();

        Assert.Throws<ArgumentNullException>(
            () => builder.WithKnowledge("produits", (Action<KnowledgeAttachmentOptions>)null!));
    }

    [Fact]
    public void WithKnowledge_NullAttachment_Throws()
    {
        var builder = MinimalAgent();

        Assert.Throws<ArgumentNullException>(
            () => builder.WithKnowledge((KnowledgeAttachment)null!));
    }

    [Fact]
    public void AgentCreate_WithInvalidAttachmentInOptions_Throws()
    {
        // Bypass the factory to build an invalid record, then verify the aggregate re-validates.
        var invalid = new KnowledgeAttachment { Collection = "  " };

        Assert.Throws<ArgumentException>(() => Orkeon.Domain.Agent.Agent.Create(new AgentCreateOptions
        {
            Role = Orkeon.Domain.Agent.ValueObjects.AgentRole.From("r"),
            Goal = Orkeon.Domain.Agent.ValueObjects.AgentGoal.From("g"),
            KnowledgeAttachments = [invalid],
        }));
    }

    [Fact]
    public void AgentRestore_WithSnapshotAttachments_RehydratesThem()
    {
        var original = MinimalAgent().WithKnowledge("produits").Build();

        var restored = Orkeon.Domain.Agent.Agent.Restore(new AgentSnapshot
        {
            Id = original.Id,
            Role = original.Role,
            Goal = original.Goal,
            Status = original.Status,
            KnowledgeAttachments = original.KnowledgeAttachments,
        });

        var attachment = Assert.Single(restored.KnowledgeAttachments);
        Assert.Equal("produits", attachment.Collection);
    }
}
