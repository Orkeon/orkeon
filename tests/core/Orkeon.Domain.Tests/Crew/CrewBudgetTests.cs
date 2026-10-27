using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// #218 - the budget of an autonomous crew comes from the crew (<c>budget:</c> in YAML,
/// <c>crewBuilder().budget(...)</c> in a script). A dimension left out bounds nothing that would stop an
/// ordinary crew: no token cap and no wall time unless one is set.
/// </summary>
public sealed class CrewBudgetTests
{
    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldSetNoTokenCapNorWallTime_WhenTheCrewSetsNone()
    {
        // Act
        var budget = CrewBudget.Unset.ToExecutionBudget();

        // Assert
        Assert.Equal(int.MaxValue, budget.MaxTokensConsumed);
        Assert.Equal(TimeSpan.MaxValue, budget.MaxWallTime);
        Assert.Equal(CrewBudget.DefaultToolCalls, budget.MaxToolCalls);
        Assert.Equal(CrewBudget.DefaultDelegationDepth, budget.MaxDelegationDepth);
        Assert.Equal(CrewBudget.DefaultSpawnedAgents, budget.MaxSpawnedAgents);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldBoundTheRunByWhatTheCrewSets()
    {
        // Arrange
        var crewBudget = new CrewBudget
        {
            Tokens = 500_000, WallTime = TimeSpan.FromMinutes(30), ToolCalls = 80, DelegationDepth = 2, SpawnedAgents = 3,
        };

        // Act
        var budget = crewBudget.ToExecutionBudget();

        // Assert
        Assert.Equal(500_000, budget.MaxTokensConsumed);
        Assert.Equal(TimeSpan.FromMinutes(30), budget.MaxWallTime);
        Assert.Equal(80, budget.MaxToolCalls);
        Assert.Equal(2, budget.MaxDelegationDepth);
        Assert.Equal(3, budget.MaxSpawnedAgents);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldGiveEachRunAFreshBudget()
    {
        // Arrange
        var crewBudget = new CrewBudget { Tokens = 1_000 };
        var first = crewBudget.ToExecutionBudget();
        first.RecordTokens(400);

        // Act
        var second = crewBudget.ToExecutionBudget();

        // Assert
        Assert.Equal(0, second.CurrentTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldCarryTheBudget_WhenAnAutonomousCrewIsCreated()
    {
        // Act
        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "goal", ProcessType = ProcessType.Autonomous, Budget = new CrewBudget { Tokens = 200_000 },
        });

        // Assert
        Assert.Equal(200_000, crew.Budget.Tokens);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldHaveAnUnsetBudget_WhenTheCrewSetsNone()
    {
        var crew = DomainCrew.Create("goal", ProcessType.Autonomous);

        Assert.Same(CrewBudget.Unset, crew.Budget);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldRefuseABudget_WhenTheProcessIsNotAutonomous()
    {
        var error = Assert.Throws<ArgumentException>(() => DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "goal", ProcessType = ProcessType.Sequential, Budget = new CrewBudget { Tokens = 200_000 },
        }));

        Assert.Contains("budget", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sequential", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-218")]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(null, 0)]
    public void ShouldRefuseALimitOfZeroOrLess(int? tokens, int? wallTimeSeconds)
    {
        var budget = new CrewBudget
        {
            Tokens = tokens, WallTime = wallTimeSeconds is { } s ? TimeSpan.FromSeconds(s) : null,
        };

        Assert.Throws<ArgumentException>(() => DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "goal", ProcessType = ProcessType.Autonomous, Budget = budget,
        }));
    }
}
