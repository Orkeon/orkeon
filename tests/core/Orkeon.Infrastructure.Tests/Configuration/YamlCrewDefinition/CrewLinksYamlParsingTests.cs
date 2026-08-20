using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.EventHub;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// HUB-03: the <c>links:</c> block is how a crew declares who it may talk to on the EventHub.
/// Parsing only — enforcement is the ACL middleware's job, and the registry's.
/// </summary>
public class CrewLinksYamlParsingTests
{
    private static YamlCrewDefinitionLoader BuildLoader()
        => new(
            new YamlDotNetSerializer(),
            new FakeFileSystemService(),
            NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task LoadFromString_MapsEveryFieldOfTheBlock()
    {
        var yaml = """
name: billing
goal: x
links:
  - to: fraud
    direction: bidirectional
    allowed_topics: [fraud.check, fraud.result]
  - to: "client:studio"
    direction: outbound
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(2, config.Links.Count);

        var fraud = config.Links[0];
        Assert.Equal("fraud", fraud.To);
        Assert.Equal(CrewLinkDirection.Bidirectional, fraud.Direction);
        Assert.Equal(["fraud.check", "fraud.result"], fraud.AllowedTopics);
        Assert.False(fraud.TargetsClient);

        // The external peer is named through the reserved prefix rather than pretending to be
        // a crew — the one place HUB-03 extends the spec.
        var studio = config.Links[1];
        Assert.True(studio.TargetsClient);
        Assert.Equal("studio", studio.ClientName);
        Assert.True(studio.Authorizes("anything"));
    }

    [Fact]
    public async Task LoadFromString_WithoutTheBlock_LeavesTheCrewUndeclared()
    {
        var yaml = """
name: billing
goal: x
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Empty(config.Links);
    }

    [Fact]
    public async Task LoadFromString_DefaultsToTheNarrowestDirection()
    {
        // An omitted or unreadable direction must not widen the authorization: outbound is the
        // narrowest of the three, and a typo should cost access rather than grant it.
        var yaml = """
name: billing
goal: x
links:
  - to: fraud
  - to: audit
    direction: sideways
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.All(config.Links, link => Assert.Equal(CrewLinkDirection.Outbound, link.Direction));
    }

    [Fact]
    public async Task LoadFromString_DropsAnEntryThatNamesNobody()
    {
        // A link without a target authorizes nothing in particular; keeping it would only
        // create a rule that can never match, and a malformed authorization must never become
        // a permissive one.
        var yaml = """
name: billing
goal: x
links:
  - direction: bidirectional
    allowed_topics: [fraud.check]
  - to: fraud
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var link = Assert.Single(config.Links);
        Assert.Equal("fraud", link.To);
    }
}
