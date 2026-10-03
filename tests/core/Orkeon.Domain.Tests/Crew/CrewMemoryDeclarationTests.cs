using Orkeon.Domain.Crew;
using CrewAggregate = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// GAP-30 — <c>memory:</c> commands whether a crew stores and recalls, <c>memoryProvider:</c> where.
/// A provider named for a crew without memory would hold nothing: the crew refuses it, and says how
/// to fix it.
/// </summary>
public sealed class CrewMemoryDeclarationTests
{
    [Fact]
    public void A_provider_without_memory_is_refused_with_the_remedy()
    {
        var error = Assert.Throws<ArgumentException>(() => CrewAggregate.Create(new CrewCreateOptions
        {
            Goal = "Watch the contracts",
            MemoryProvider = "sqlite",
        }));

        Assert.Contains("sqlite", error.Message, StringComparison.Ordinal);
        Assert.Contains("memory: true", error.Message, StringComparison.Ordinal);
        Assert.Contains("EnableMemory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_refuses_a_provider_without_memory_too()
    {
        var builder = new CrewBuilder().Goal("Watch the contracts").WithMemoryProvider("sqlite");

        var error = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.Contains("memory: true", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_with_memory_is_accepted()
    {
        var crew = new CrewBuilder().Goal("Watch the contracts").EnableMemory().WithMemoryProvider("sqlite").Build();

        Assert.True(crew.MemoryEnabled);
        Assert.Equal("sqlite", crew.MemoryProvider);
    }

    [Fact]
    public void Memory_without_a_provider_is_accepted_and_lives_in_the_host_default()
    {
        var crew = new CrewBuilder().Goal("Watch the contracts").EnableMemory().Build();

        Assert.True(crew.MemoryEnabled);
        Assert.Null(crew.MemoryProvider);
    }

    [Fact]
    public void Memory_is_off_by_default()
    {
        var crew = new CrewBuilder().Goal("Watch the contracts").Build();

        Assert.False(crew.MemoryEnabled);
    }
}
