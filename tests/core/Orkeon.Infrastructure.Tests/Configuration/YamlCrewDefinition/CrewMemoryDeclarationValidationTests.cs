using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-30 — <c>memoryProvider:</c> says where a crew's memory lives; <c>memory: true</c> whether it
/// has one. A provider named without memory would hold nothing: the load refuses it, with the remedy.
/// </summary>
public class CrewMemoryDeclarationValidationTests
{
    private static CrewConfiguration Config(bool memory, string? memoryProvider)
    {
        var agent = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "legal-watch",
            Goal = "Watch the contracts",
            Process = ProcessType.Sequential,
            Memory = memory,
            MemoryProvider = memoryProvider,
            Agents = [new AgentConfiguration { Id = agent, Role = "lawyer", Goal = "read", Backstory = "b" }],
            Tasks = [new TaskConfiguration { Id = TaskId.Create(), Description = "d", ExpectedOutput = "o", AssignedAgentId = agent }],
        };
    }

    [Fact]
    public void A_memory_provider_without_memory_is_refused_with_the_remedy()
    {
        var result = CrewDefinitionValidator.Validate(Config(memory: false, memoryProvider: "sqlite"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("memoryProvider", error, StringComparison.Ordinal);
        Assert.Contains("sqlite", error, StringComparison.Ordinal);
        Assert.Contains("memory: true", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40, decision 6 — a provider type no provider answers to fails the load, like an unknown
    /// tool, with the known types: it used to run the crew's memory on the volatile provider.
    /// </summary>
    [Fact]
    public void A_memory_provider_no_provider_answers_to_is_refused_with_the_known_types()
    {
        var result = CrewDefinitionValidator.Validate(Config(memory: true, memoryProvider: "redsi"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("memoryProvider", error, StringComparison.Ordinal);
        Assert.Contains("redsi", error, StringComparison.Ordinal);
        Assert.Contains("redis", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "sqlite")]
    [InlineData(true, "SQLite")]
    [InlineData(true, "chroma")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void Memory_with_or_without_a_provider_and_no_memory_at_all_are_accepted(bool memory, string? memoryProvider)
    {
        Assert.True(CrewDefinitionValidator.Validate(Config(memory, memoryProvider)).IsValid);
    }
}
