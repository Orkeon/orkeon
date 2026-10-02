using Orkeon.Application.Memory;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.Memory;

/// <summary>
/// GAP-20, GAP-30 — the registry the orchestrator fills at kickoff holds what a crew declared about
/// its memory: whether it remembers at all, the provider type, and the name that scopes its
/// long-term memory.
/// </summary>
public class CrewMemoryProviderRegistryTests
{
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly CrewId _crew = CrewId.From(Guid.NewGuid());

    [Fact]
    public void The_scope_of_a_named_crew_is_its_name_trimmed()
    {
        _registry.Record(_crew, "sqlite", "  legal-watch ", memoryEnabled: true);

        Assert.Equal("sqlite", _registry.GetProvider(_crew));
        Assert.Equal("legal-watch", _registry.GetScope(_crew));
        Assert.Equal("legal-watch", _registry.GetName(_crew));
    }

    [Fact]
    public void The_scope_of_an_unnamed_crew_is_its_id()
    {
        _registry.Record(_crew, "sqlite", crewName: "  ", memoryEnabled: true);

        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
        Assert.Null(_registry.GetName(_crew));
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
        _registry.Record(_crew, providerType: null, "legal-watch", memoryEnabled: true);

        Assert.Null(_registry.GetProvider(_crew));
        Assert.Equal("legal-watch", _registry.GetScope(_crew));
    }

    [Fact]
    public void A_crew_remembers_only_when_it_was_recorded_with_memory_on()
    {
        var silent = CrewId.Create();
        var unknown = CrewId.Create();
        _registry.Record(_crew, providerType: null, "legal-watch", memoryEnabled: true);
        _registry.Record(silent, providerType: null, "legal-watch", memoryEnabled: false);

        Assert.True(_registry.IsMemoryEnabled(_crew));
        Assert.False(_registry.IsMemoryEnabled(silent));
        Assert.False(_registry.IsMemoryEnabled(unknown));
    }

    [Fact]
    public void A_crew_with_memory_but_neither_a_type_nor_a_name_is_still_recorded()
    {
        _registry.Record(_crew, providerType: null, crewName: null, memoryEnabled: true);

        Assert.True(_registry.IsMemoryEnabled(_crew));
        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }

    [Fact]
    public void Recording_neither_memory_nor_a_type_nor_a_name_forgets_the_crew()
    {
        _registry.Record(_crew, "sqlite", "legal-watch", memoryEnabled: true);

        _registry.Record(_crew, providerType: " ", crewName: null, memoryEnabled: false);

        Assert.Null(_registry.GetProvider(_crew));
        Assert.False(_registry.IsMemoryEnabled(_crew));
        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }

    [Fact]
    public void Removing_a_crew_forgets_its_memory_its_type_and_its_name()
    {
        _registry.Record(_crew, "sqlite", "legal-watch", memoryEnabled: true);

        _registry.Remove(_crew);

        Assert.Null(_registry.GetProvider(_crew));
        Assert.False(_registry.IsMemoryEnabled(_crew));
        Assert.Equal(_crew.ToString(), _registry.GetScope(_crew));
    }
}
