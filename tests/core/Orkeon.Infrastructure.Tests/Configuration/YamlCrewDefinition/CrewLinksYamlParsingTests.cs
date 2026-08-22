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

        Assert.NotNull(config.Links);
        Assert.Equal(2, config.Links!.Count);

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
        // Null, not empty: "never wrote the block" and "wrote one whose entries all failed"
        // are different situations, and the ACL treats only the first as policy-arbitrated.
        var yaml = """
name: billing
goal: x
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Null(config.Links);
    }

    [Fact]
    public async Task LoadFromString_AnOmittedDirectionIsOutbound()
    {
        // Declaring `to:` is declaring the intent to talk to them — outbound is what the
        // author meant when they wrote nothing more.
        var yaml = """
name: billing
goal: x
links:
  - to: fraud
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var link = Assert.Single(config.Links!);
        Assert.Equal(CrewLinkDirection.Outbound, link.Direction);
    }

    [Fact]
    public async Task LoadFromString_DropsAnEntryWithAnUnreadableDirection()
    {
        // A direction nobody can read is a typo in an authorization. Guessing one — any of
        // the three — would grant something the author never wrote: outbound opens the door
        // out, inbound grants the peer a way in. The entry is dropped with a warning, and the
        // block's presence still closes the door (the list survives, just without this rule).
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

        var link = Assert.Single(config.Links!);
        Assert.Equal("fraud", link.To);
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

        var link = Assert.Single(config.Links!);
        Assert.Equal("fraud", link.To);
    }

    [Fact]
    public async Task LoadFromString_ABlockWhoseEntriesAllFail_StaysDeclaredAndEmpty()
    {
        // The fail-open this pins shut: every entry malformed → empty list → if that read as
        // "never declared", a permissive deployment would give the crew full access precisely
        // because its authorizations were unreadable.
        var yaml = """
name: billing
goal: x
links:
  - direction: bidirectional
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.NotNull(config.Links);
        Assert.Empty(config.Links!);
    }
}
