using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class DelegationStrategyTests
{
    private static readonly string[] TwoConstraints = ["constraint1", "constraint2"];
    private static readonly string[] SingleConstraint = ["constraint1"];
    private static readonly string[] ConstraintC1 = ["c1"];
    private static readonly string[] ConstraintC2 = ["c2"];
    [Fact]
    public void ShouldCreateStrategy_WhenConstructingWithValidParameters()
    {
        // Arrange
        var name = "TestStrategy";
        var type = DelegationType.SkillBased;
        var parameters = DelegationParameters.ForSkillBased(0.9);
        var priority = 0.8;
        var constraints = TwoConstraints;

        // Act
        var strategy = DelegationStrategy.Create(name, type, parameters, priority, constraints);

        // Assert
        Assert.Equal(name, strategy.Name);
        Assert.Equal(type, strategy.Type);
        Assert.Equal(parameters, strategy.Parameters);
        Assert.Equal(priority, strategy.Priority);
        Assert.Equal(2, strategy.Constraints.Count);
        Assert.Contains("constraint1", strategy.Constraints);
        Assert.Contains("constraint2", strategy.Constraints);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DelegationStrategy.Create(null!, DelegationType.SkillBased));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldUseEmpty_WhenConstructingWithNullParameters()
    {
        // Act
        var strategy = DelegationStrategy.Create("Test", DelegationType.RoundRobin, parameters: null);

        // Assert
        Assert.NotNull(strategy.Parameters);
        Assert.Same(DelegationParameters.Empty, strategy.Parameters);
    }

    [Fact]
    public void ShouldUseEmptyList_WhenConstructingWithNullConstraints()
    {
        // Act
        var strategy = DelegationStrategy.Create("Test", DelegationType.RoundRobin, constraints: null);

        // Assert
        Assert.NotNull(strategy.Constraints);
        Assert.Empty(strategy.Constraints);
    }

    [Fact]
    public void ShouldBe1_WhenConstructingWithDefaultPriority()
    {
        // Act
        var strategy = DelegationStrategy.Create("Test", DelegationType.RoundRobin);

        // Assert
        Assert.Equal(1.0, strategy.Priority);
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingSkillBasedWithDefaultValue()
    {
        // Act
        var strategy = DelegationStrategy.SkillBased();

        // Assert
        Assert.Equal("SkillBased", strategy.Name);
        Assert.Equal(DelegationType.SkillBased, strategy.Type);
        Assert.Equal(0.8, strategy.Parameters.Get<double>("minSkillMatch"));
        Assert.Equal(1.0, strategy.Priority);
        Assert.Empty(strategy.Constraints);
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingSkillBasedWithCustomValue()
    {
        // Act
        var strategy = DelegationStrategy.SkillBased(0.95);

        // Assert
        Assert.Equal("SkillBased", strategy.Name);
        Assert.Equal(DelegationType.SkillBased, strategy.Type);
        Assert.Equal(0.95, strategy.Parameters.Get<double>("minSkillMatch"));
    }

    [Fact]
    public void ShouldBeAliasForSkillBased_WhenUsingCapabilityBased()
    {
        // Act
        var skillBased = DelegationStrategy.SkillBased(0.85);
        var capabilityBased = DelegationStrategy.CapabilityBased(0.85);

        // Assert
        Assert.Equal(skillBased.Name, capabilityBased.Name);
        Assert.Equal(skillBased.Type, capabilityBased.Type);
        Assert.Equal(
            skillBased.Parameters.Get<double>("minSkillMatch"),
            capabilityBased.Parameters.Get<double>("minSkillMatch"));
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingWorkloadBasedWithDefaultValue()
    {
        // Act
        var strategy = DelegationStrategy.WorkloadBased();

        // Assert
        Assert.Equal("WorkloadBased", strategy.Name);
        Assert.Equal(DelegationType.WorkloadBased, strategy.Type);
        Assert.Equal(5, strategy.Parameters.Get<int>("maxWorkload"));
        Assert.Equal(1.0, strategy.Priority);
        Assert.Empty(strategy.Constraints);
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingWorkloadBasedWithCustomValue()
    {
        // Act
        var strategy = DelegationStrategy.WorkloadBased(10);

        // Assert
        Assert.Equal("WorkloadBased", strategy.Name);
        Assert.Equal(DelegationType.WorkloadBased, strategy.Type);
        Assert.Equal(10, strategy.Parameters.Get<int>("maxWorkload"));
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingRoundRobin()
    {
        // Act
        var strategy = DelegationStrategy.RoundRobin();

        // Assert
        Assert.Equal("RoundRobin", strategy.Name);
        Assert.Equal(DelegationType.RoundRobin, strategy.Type);
        Assert.Same(DelegationParameters.Empty, strategy.Parameters);
        Assert.Equal(1.0, strategy.Priority);
        Assert.Empty(strategy.Constraints);
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingHierarchical()
    {
        // Arrange
        var managerRole = "ProjectManager";

        // Act
        var strategy = DelegationStrategy.Hierarchical(managerRole);

        // Assert
        Assert.Equal("Hierarchical", strategy.Name);
        Assert.Equal(DelegationType.Hierarchical, strategy.Type);
        Assert.Equal(managerRole, strategy.Parameters.Get<string>("managerRole"));
        Assert.Equal(1.0, strategy.Priority);
        Assert.Empty(strategy.Constraints);
    }

    [Fact]
    public void ShouldCreateCorrectStrategy_WhenUsingCustom()
    {
        // Arrange
        var name = "MyCustomStrategy";
        var parameters = DelegationParameters.CreateBuilder()
            .Add("customParam", "value")
            .Add("threshold", 0.7)
            .Build();

        // Act
        var strategy = DelegationStrategy.Custom(name, parameters);

        // Assert
        Assert.Equal(name, strategy.Name);
        Assert.Equal(DelegationType.Custom, strategy.Type);
        Assert.Equal(parameters, strategy.Parameters);
        Assert.Equal("value", strategy.Parameters.Get<string>("customParam"));
        Assert.Equal(0.7, strategy.Parameters.Get<double>("threshold"));
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var parameters = DelegationParameters.ForSkillBased(0.9);
        var constraints = SingleConstraint;

        var strategy1 = DelegationStrategy.Create("Test", DelegationType.SkillBased, parameters, 0.8, constraints);
        var strategy2 = DelegationStrategy.Create("Test", DelegationType.SkillBased, parameters, 0.8, constraints);

        // Act & Assert
        Assert.Equal(strategy1, strategy2);
        Assert.True(strategy1 == strategy2);
        Assert.False(strategy1 != strategy2);
        Assert.Equal(strategy1.GetHashCode(), strategy2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentNames()
    {
        // Arrange
        var strategy1 = DelegationStrategy.SkillBased();
        var strategy2 = DelegationStrategy.Create("Different", DelegationType.SkillBased);

        // Act & Assert
        Assert.NotEqual(strategy1, strategy2);
        Assert.False(strategy1 == strategy2);
        Assert.True(strategy1 != strategy2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentTypes()
    {
        // Arrange
        var strategy1 = DelegationStrategy.Create("Test", DelegationType.SkillBased);
        var strategy2 = DelegationStrategy.Create("Test", DelegationType.WorkloadBased);

        // Act & Assert
        Assert.NotEqual(strategy1, strategy2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentPriorities()
    {
        // Arrange
        var strategy1 = DelegationStrategy.Create("Test", DelegationType.RoundRobin, priority: 0.8);
        var strategy2 = DelegationStrategy.Create("Test", DelegationType.RoundRobin, priority: 0.9);

        // Act & Assert
        Assert.NotEqual(strategy1, strategy2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentConstraints()
    {
        // Arrange
        var strategy1 = DelegationStrategy.Create("Test", DelegationType.RoundRobin, constraints: ConstraintC1);
        var strategy2 = DelegationStrategy.Create("Test", DelegationType.RoundRobin, constraints: ConstraintC2);

        // Act & Assert
        Assert.NotEqual(strategy1, strategy2);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithModifiedProperty_WhenRecordingWith()
    {
        // Arrange
        var original = DelegationStrategy.SkillBased(0.8);

        // Act
        var modified = DelegationStrategy.Create(
            original.Name,
            original.Type,
            original.Parameters,
            priority: 0.5,
            constraints: original.Constraints);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(original.Name, modified.Name);
        Assert.Equal(original.Type, modified.Type);
        Assert.Equal(original.Parameters, modified.Parameters);
        Assert.Equal(0.5, modified.Priority); // Changed
        Assert.Equal(original.Constraints, modified.Constraints);
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingConstraints()
    {
        // Arrange
        var mutableList = new List<string> { "constraint1", "constraint2" };
        var strategy = DelegationStrategy.Create("Test", DelegationType.RoundRobin, constraints: mutableList);

        // Act - Modify original list
        mutableList.Add("constraint3");

        // Assert - Strategy constraints should not change
        Assert.Equal(2, strategy.Constraints.Count);
        Assert.DoesNotContain("constraint3", strategy.Constraints);
    }

    [Fact]
    public void ShouldReturnMeaningfulRepresentation_WhenCallingToString()
    {
        // Arrange
        var strategy = DelegationStrategy.SkillBased(0.9);

        // Act
        var result = strategy.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("DelegationStrategy", result);
        // Record ToString includes property names and values
    }

    [Fact]
    public void ShouldCombiningStrategiesWithPriorities_WhenUsingComplexScenario()
    {
        // Arrange - Create multiple strategies with different priorities
        var strategies = new[]
        {
            DelegationStrategy.Create(
                DelegationStrategy.SkillBased(0.9).Name,
                DelegationStrategy.SkillBased(0.9).Type,
                DelegationStrategy.SkillBased(0.9).Parameters,
                priority: 1.0),
            DelegationStrategy.Create(
                DelegationStrategy.WorkloadBased(3).Name,
                DelegationStrategy.WorkloadBased(3).Type,
                DelegationStrategy.WorkloadBased(3).Parameters,
                priority: 0.8),
            DelegationStrategy.Create(
                DelegationStrategy.RoundRobin().Name,
                DelegationStrategy.RoundRobin().Type,
                DelegationStrategy.RoundRobin().Parameters,
                priority: 0.5),
            DelegationStrategy.Create(
                "Emergency",
                DelegationType.Custom,
                DelegationParameters.CreateBuilder()
                    .Add("urgency", "high")
                    .Build(),
                priority: 2.0)
        };

        // Act - Sort by priority (highest first)
        var sorted = strategies.OrderByDescending(s => s.Priority).ToList();

        // Assert
        Assert.Equal("Emergency", sorted[0].Name); // Priority 2.0
        Assert.Equal("SkillBased", sorted[1].Name); // Priority 1.0
        Assert.Equal("WorkloadBased", sorted[2].Name); // Priority 0.8
        Assert.Equal("RoundRobin", sorted[3].Name); // Priority 0.5
    }

    [Fact]
    public void ShouldStrategyWithConstraints_WhenUsingComplexScenario()
    {
        // Arrange
        var constraints = new[]
        {
            "max_retries:3",
            "timeout:30s",
            "require_senior_agent",
            "no_external_api_calls"
        };

        var strategy = DelegationStrategy.Create(
            "ConstrainedStrategy",
            DelegationType.SkillBased,
            DelegationParameters.ForSkillBased(0.95),
            0.9,
            constraints
        );

        // Act & Assert
        Assert.Equal(4, strategy.Constraints.Count);
        Assert.All(constraints, c => Assert.Contains(c, strategy.Constraints));

        // Constraints are immutable - Add returns a new list, doesn't mutate
        var newList = strategy.Constraints.Add("new_constraint");
        Assert.NotSame(strategy.Constraints, newList);
        Assert.Equal(4, strategy.Constraints.Count); // Original unchanged
        Assert.Equal(5, newList.Count); // New list has additional item
    }

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingDelegationType()
    {
        // Assert — sealed records use string values; verify All contains expected members
        Assert.Equal(5, DelegationType.All.Count);
        Assert.Equal("SkillBased", DelegationType.SkillBased.Value);
        Assert.Equal("WorkloadBased", DelegationType.WorkloadBased.Value);
        Assert.Equal("RoundRobin", DelegationType.RoundRobin.Value);
        Assert.Equal("Hierarchical", DelegationType.Hierarchical.Value);
        Assert.Equal("Custom", DelegationType.Custom.Value);
    }

    [Fact]
    public void ShouldCreateDistinctStrategies_WhenUsingFactoryMethods()
    {
        // Act
        var strategies = new[]
        {
            DelegationStrategy.SkillBased(),
            DelegationStrategy.WorkloadBased(),
            DelegationStrategy.RoundRobin(),
            DelegationStrategy.Hierarchical(RoleManager),
            DelegationStrategy.Custom("Custom", DelegationParameters.Empty)
        };

        // Assert - All strategies should be distinct
        Assert.Equal(5, strategies.Distinct().Count());

        // Each should have the correct type
        Assert.Equal(DelegationType.SkillBased, strategies[0].Type);
        Assert.Equal(DelegationType.WorkloadBased, strategies[1].Type);
        Assert.Equal(DelegationType.RoundRobin, strategies[2].Type);
        Assert.Equal(DelegationType.Hierarchical, strategies[3].Type);
        Assert.Equal(DelegationType.Custom, strategies[4].Type);
    }
}
