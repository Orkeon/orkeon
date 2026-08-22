using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.EventHub;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.EventHub;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.EventHub;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.EventHub;

/// <summary>
/// HUB-03: a crew's <c>links:</c> block is read from YAML long before the crew has an identity,
/// so the authorizations only reach the ACL once <see cref="CrewFactory"/> has created it.
/// These tests cover that handover, and the registry that receives it.
/// </summary>
public class CrewLinkRegistrationTests
{
    private static CrewFactory BuildFactory(ICrewLinkRegistry? registry)
    {
        var loader = new MockCrewDefinitionLoader();
        loader.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        return new CrewFactory(
            loader,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork),
            Options.Create(new CrewFactoryOptions { PrepareRagCollections = false }),
            ragBootstrapper: null,
            linkRegistry: registry);
    }

    private static CrewConfiguration Configuration(params CrewLink[] links)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "billing",
            Goal = "test",
            Links = links.Length == 0 ? null : links,
            Agents = [new AgentConfiguration { Id = agentId, Role = "worker", Goal = "do work", Backstory = "b" }],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "do something",
                    ExpectedOutput = "output",
                    AssignedAgentId = agentId,
                }
            ],
        };
    }

    [Fact]
    public async Task The_factory_hands_the_declared_links_over_under_the_new_crew_id()
    {
        var registry = new InMemoryCrewLinkRegistry();
        var factory = BuildFactory(registry);

        var crew = await factory.CreateFromConfigAsync(
            Configuration(new CrewLink { To = "fraud", AllowedTopics = ["fraud.check"] }),
            TestContext.Current.CancellationToken);

        var link = Assert.Single(registry.LinksFor(crew.Id));
        Assert.Equal("fraud", link.To);
        Assert.Equal("billing", registry.NameOf(crew.Id));
    }

    [Fact]
    public async Task A_crew_declaring_nothing_still_registers_its_name()
    {
        // Links name crews by name while messages carry ids, so a crew that declared nothing
        // must still be resolvable as somebody *else's* target — otherwise `to: billing`
        // written by another crew could never match. Its own links stay "never declared"
        // (default), which the policy arbitrates.
        var registry = new InMemoryCrewLinkRegistry();
        var factory = BuildFactory(registry);

        var crew = await factory.CreateFromConfigAsync(
            Configuration(), TestContext.Current.CancellationToken);

        Assert.True(registry.LinksFor(crew.Id).IsDefault);
        Assert.Equal("billing", registry.NameOf(crew.Id));
    }

    [Fact]
    public async Task Declaring_links_without_the_ACL_registered_still_creates_the_crew()
    {
        // The author wrote an authorization that nothing will enforce. That is worth a warning
        // — CrewFactory logs one — but not a failed crew: the ACL is opt-in, and refusing here
        // would make declaring links a trap rather than a precaution.
        var factory = BuildFactory(registry: null);

        var crew = await factory.CreateFromConfigAsync(
            Configuration(new CrewLink { To = "fraud" }), TestContext.Current.CancellationToken);

        Assert.NotNull(crew);
    }

    [Fact]
    public void The_last_declaration_wins()
    {
        // Recreating a crew from an edited configuration must not leave the door open on what
        // the previous version declared.
        var registry = new InMemoryCrewLinkRegistry();
        var crewId = CrewId.Create();

        registry.Register(crewId, "billing", [new CrewLink { To = "fraud" }]);
        registry.Register(crewId, "billing", [new CrewLink { To = "audit" }]);

        var link = Assert.Single(registry.LinksFor(crewId));
        Assert.Equal("audit", link.To);
    }

    [Fact]
    public void An_unknown_crew_reads_as_never_declared_and_has_no_name()
    {
        var registry = new InMemoryCrewLinkRegistry();
        var unknown = CrewId.Create();

        Assert.True(registry.LinksFor(unknown).IsDefault);
        Assert.Null(registry.NameOf(unknown));
    }

    [Fact]
    public void A_declared_empty_list_is_not_the_same_as_never_declaring()
    {
        // Empty-after-parsing is what a malformed links: block collapses to; the ACL refuses
        // on it. Collapsing it to "never declared" would turn a malformed authorization into
        // a permissive one — the exact failure the grammar documentation forbids.
        var registry = new InMemoryCrewLinkRegistry();
        var crewId = CrewId.Create();

        registry.Register(crewId, "billing", []);

        var links = registry.LinksFor(crewId);
        Assert.False(links.IsDefault);
        Assert.True(links.IsEmpty);
    }
}
