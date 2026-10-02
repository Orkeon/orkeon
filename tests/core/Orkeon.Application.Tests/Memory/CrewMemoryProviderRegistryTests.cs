using Orkeon.Application.Memory;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.Memory;

/// <summary>
/// GAP-20 — the registry the orchestrator fills at kickoff holds what a crew declared about its
/// memory: the provider type, and the name that scopes its long-term memory.
/// </summary>
public class CrewMemoryProviderRegistryTests
{
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly CrewId _crew = CrewId.From(Guid.NewGuid());

    [Fact]
    public void The_scope_of_a_named_crew_is_its_name_trimmed()
    {
        _registry.Record(_crew, "sqlite", "  legal-watch ");

        Assert.Equal("sqlite", _registry.GetProvider(_crew));
        Assert.Equal("legal-watch", _registry.GetScope(_crew));
    }

    [Fact]
    public void The_scope_of_an_unnamed_crew_is_its_id()
    {
        _registry.Record(_crew, "sqlite", crewName: "  ");

        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }

    [Fact]
    public void The_scope_of_an_unknown_crew_is_its_id()
    {
        Assert.Null(_registry.GetProvider(_crew));
        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }

    [Fact]
    public void A_crew_on_the_default_store_keeps_its_name()
    {
        _registry.Record(_crew, providerType: null, "legal-watch");

        Assert.Null(_registry.GetProvider(_crew));
        Assert.Equal("legal-watch", _registry.GetScope(_crew));
    }

    [Fact]
    public void Recording_neither_a_type_nor_a_name_forgets_the_crew()
    {
        _registry.Record(_crew, "sqlite", "legal-watch");

        _registry.Record(_crew, providerType: " ", crewName: null);

        Assert.Null(_registry.GetProvider(_crew));
        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }

    [Fact]
    public void Removing_a_crew_forgets_its_type_and_its_name()
    {
        _registry.Record(_crew, "sqlite", "legal-watch");

        _registry.Remove(_crew);

        Assert.Null(_registry.GetProvider(_crew));
        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }
}
