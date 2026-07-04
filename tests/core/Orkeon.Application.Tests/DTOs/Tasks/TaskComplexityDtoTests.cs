using System.Collections.Immutable;
using Orkeon.Application.Task.DTOs;

namespace Orkeon.Application.Tests.DTOs.Tasks;

public class TaskComplexityDtoTests
{
    private static readonly string[] ComplexSkills =
    [
        "Microservices Architecture",
        "Event-Driven Design",
        "Distributed Systems",
        "Kubernetes",
        "Service Mesh"
    ];
    private static readonly string[] ComplexDependencies =
    [
        "Core Platform",
        "Message Bus",
        "Service Discovery",
        "Configuration Service"
    ];
    private static readonly string[] ComplexRiskFactors =
    [
        "Network latency",
        "Data consistency",
        "Service coordination"
    ];

    private static readonly string[] s_requiredSkills = ["C#", "Azure", "Docker"];
    private static readonly string[] s_dependencies = ["Database", "API", "Auth"];
    private static readonly string[] s_riskFactors = ["Third-party integration", "Performance"];
    private static readonly string[] s_serviceDependencies = ["User Service", "Payment Gateway", "Notification Service", "Analytics Service"];

    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new TaskComplexityDto
        {
            Level = "High"
        };

        // Assert
        Assert.Equal("High", dto.Level);
        Assert.Equal(0, dto.EstimatedEffort);
        Assert.Equal(TimeSpan.Zero, dto.EstimatedDuration);
        Assert.True(dto.RequiredSkills.IsEmpty);
        Assert.True(dto.Dependencies.IsEmpty);
        Assert.True(dto.RiskFactors.IsEmpty);
        Assert.Equal(0.0, dto.ComplexityScore);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenConstructingWithAllProperties()
    {
        // Arrange
        var requiredSkills = ImmutableList<string>.Empty.AddRange(s_requiredSkills);
        var dependencies = ImmutableList<string>.Empty.AddRange(s_dependencies);
        var riskFactors = ImmutableList<string>.Empty.AddRange(s_riskFactors);

        // Act
        var dto = new TaskComplexityDto
        {
            Level = "Complex",
            EstimatedEffort = 8,
            EstimatedDuration = TimeSpan.FromHours(16),
            RequiredSkills = requiredSkills,
            Dependencies = dependencies,
            RiskFactors = riskFactors,
            ComplexityScore = 8.5
        };

        // Assert
        Assert.Equal("Complex", dto.Level);
        Assert.Equal(8, dto.EstimatedEffort);
        Assert.Equal(TimeSpan.FromHours(16), dto.EstimatedDuration);
        Assert.Equal(3, dto.RequiredSkills.Count);
        Assert.Contains("C#", dto.RequiredSkills);
        Assert.Contains("Azure", dto.RequiredSkills);
        Assert.Contains("Docker", dto.RequiredSkills);
        Assert.Equal(3, dto.Dependencies.Count);
        Assert.Contains("Database", dto.Dependencies);
        Assert.Contains("API", dto.Dependencies);
        Assert.Contains("Auth", dto.Dependencies);
        Assert.Equal(2, dto.RiskFactors.Count);
        Assert.Contains("Third-party integration", dto.RiskFactors);
        Assert.Contains("Performance", dto.RiskFactors);
        Assert.Equal(8.5, dto.ComplexityScore);
    }

    [Theory]
    [InlineData("Simple")]
    [InlineData("Medium")]
    [InlineData("Complex")]
    [InlineData("Very Complex")]
    public void ShouldBeSettable_WhenUsingLevelWithDifferentValues(string level)
    {
        // Arrange & Act
        var dto = new TaskComplexityDto { Level = level };

        // Assert
        Assert.Equal(level, dto.Level);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void ShouldBeSettable_WhenUsingEstimatedEffortWithDifferentValues(int effort)
    {
        // Arrange & Act
        var dto = new TaskComplexityDto
        {
            Level = "Test",
            EstimatedEffort = effort
        };

        // Assert
        Assert.Equal(effort, dto.EstimatedEffort);
    }

    [Fact]
    public void ShouldAcceptTimeSpanValues_WhenUsingEstimatedDuration()
    {
        // Arrange
        var durations = new[]
        {
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(5)
        };

        foreach (var duration in durations)
        {
            // Act
            var dto = new TaskComplexityDto
            {
                Level = "Test",
                EstimatedDuration = duration
            };

            // Assert
            Assert.Equal(duration, dto.EstimatedDuration);
        }
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingRequiredSkills()
    {
        // Arrange
        var skills = ImmutableList<string>.Empty
            .Add("Backend Development")
            .Add("Database Design")
            .Add("API Development")
            .Add("Cloud Architecture")
            .Add("DevOps");

        // Act
        var dto = new TaskComplexityDto
        {
            Level = "Expert",
            RequiredSkills = skills
        };

        // Assert
        Assert.Equal(5, dto.RequiredSkills.Count);
        Assert.Equal("Backend Development", dto.RequiredSkills[0]);
        Assert.Equal("Database Design", dto.RequiredSkills[1]);
        Assert.Equal("API Development", dto.RequiredSkills[2]);
        Assert.Equal("Cloud Architecture", dto.RequiredSkills[3]);
        Assert.Equal("DevOps", dto.RequiredSkills[4]);
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingDependencies()
    {
        // Arrange
        var dependencies = ImmutableList<string>.Empty
            .Add("User Service")
            .Add("Payment Gateway")
            .Add("Notification Service")
            .Add("Analytics Service");

        // Act
        var dto = new TaskComplexityDto
        {
            Level = "High",
            Dependencies = dependencies
        };

        // Assert
        Assert.Equal(4, dto.Dependencies.Count);
        foreach (var dependency in s_serviceDependencies)
        {
            Assert.Contains(dependency, dto.Dependencies);
        }
    }

    [Fact]
    public void ShouldAcceptImmutableList_WhenUsingRiskFactors()
    {
        // Arrange
        var riskFactors = ImmutableList<string>.Empty
            .Add("Regulatory compliance")
            .Add("Security vulnerabilities")
            .Add("Performance bottlenecks")
            .Add("Data migration risks")
            .Add("Third-party API stability");

        // Act
        var dto = new TaskComplexityDto
        {
            Level = "Critical",
            RiskFactors = riskFactors
        };

        // Assert
        Assert.Equal(5, dto.RiskFactors.Count);
        Assert.Contains("Regulatory compliance", dto.RiskFactors);
        Assert.Contains("Security vulnerabilities", dto.RiskFactors);
        Assert.Contains("Performance bottlenecks", dto.RiskFactors);
        Assert.Contains("Data migration risks", dto.RiskFactors);
        Assert.Contains("Third-party API stability", dto.RiskFactors);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.5)]
    [InlineData(5.0)]
    [InlineData(7.5)]
    [InlineData(10.0)]
    public void ShouldBeSettable_WhenUsingComplexityScoreWithDifferentValues(double score)
    {
        // Arrange & Act
        var dto = new TaskComplexityDto
        {
            Level = "Test",
            ComplexityScore = score
        };

        // Assert
        Assert.Equal(score, dto.ComplexityScore);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var level = "Medium";
        var effort = 5;
        var duration = TimeSpan.FromHours(8);
        var skills = ImmutableList<string>.Empty.Add("Skill1");
        var dependencies = ImmutableList<string>.Empty.Add("Dep1");
        var risks = ImmutableList<string>.Empty.Add("Risk1");
        var score = 5.0;

        // Act
        var dto1 = new TaskComplexityDto
        {
            Level = level,
            EstimatedEffort = effort,
            EstimatedDuration = duration,
            RequiredSkills = skills,
            Dependencies = dependencies,
            RiskFactors = risks,
            ComplexityScore = score
        };

        var dto2 = new TaskComplexityDto
        {
            Level = level,
            EstimatedEffort = effort,
            EstimatedDuration = duration,
            RequiredSkills = skills,
            Dependencies = dependencies,
            RiskFactors = risks,
            ComplexityScore = score
        };

        // Assert
        Assert.Equal(dto1, dto2);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange & Act
        var dto1 = new TaskComplexityDto
        {
            Level = "Simple",
            ComplexityScore = 2.0
        };

        var dto2 = new TaskComplexityDto
        {
            Level = "Complex",
            ComplexityScore = 8.0
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

    [Fact]
    public void ShouldBeEmpty_WhenUsingEmptyCollections()
    {
        // Arrange & Act
        var dto = new TaskComplexityDto
        {
            Level = "Test"
        };

        // Assert
        Assert.NotNull(dto.RequiredSkills);
        Assert.NotNull(dto.Dependencies);
        Assert.NotNull(dto.RiskFactors);
        Assert.Empty(dto.RequiredSkills);
        Assert.Empty(dto.Dependencies);
        Assert.Empty(dto.RiskFactors);
    }

    [Fact]
    public void ShouldBeValid_WhenCompletingTaskComplexityWithAllData()
    {
        // Arrange & Act
        var dto = new TaskComplexityDto
        {
            Level = "Very Complex",
            EstimatedEffort = 21,
            EstimatedDuration = TimeSpan.FromDays(3),
            RequiredSkills = ImmutableList<string>.Empty.AddRange(ComplexSkills),
            Dependencies = ImmutableList<string>.Empty.AddRange(ComplexDependencies),
            RiskFactors = ImmutableList<string>.Empty.AddRange(ComplexRiskFactors),
            ComplexityScore = 9.2
        };

        // Assert
        Assert.Equal("Very Complex", dto.Level);
        Assert.Equal(21, dto.EstimatedEffort);
        Assert.Equal(TimeSpan.FromDays(3), dto.EstimatedDuration);
        Assert.Equal(5, dto.RequiredSkills.Count);
        Assert.Equal(4, dto.Dependencies.Count);
        Assert.Equal(3, dto.RiskFactors.Count);
        Assert.Equal(9.2, dto.ComplexityScore);
    }

    [Fact]
    public void ShouldNotBeModifiable_WhenUsingImmutableCollections()
    {
        // Arrange
        var dto = new TaskComplexityDto
        {
            Level = "Test",
            RequiredSkills = ImmutableList<string>.Empty.Add("Skill1")
        };

        // Act
        var originalCount = dto.RequiredSkills.Count;
        var newSkills = dto.RequiredSkills.Add("Skill2");

        // Assert
        Assert.Single(dto.RequiredSkills); // Original unchanged
        Assert.Equal(originalCount, dto.RequiredSkills.Count);
        Assert.Equal(2, newSkills.Count); // New list has additional item
        Assert.NotSame(dto.RequiredSkills, newSkills);
    }

    [Fact]
    public void ShouldHaveDefaultValues_WhenUsingMinimalComplexity()
    {
        // Arrange & Act
        var dto = new TaskComplexityDto
        {
            Level = "Minimal"
        };

        // Assert
        Assert.Equal("Minimal", dto.Level);
        Assert.Equal(0, dto.EstimatedEffort);
        Assert.Equal(TimeSpan.Zero, dto.EstimatedDuration);
        Assert.Equal(0.0, dto.ComplexityScore);
        Assert.True(dto.RequiredSkills.IsEmpty);
        Assert.True(dto.Dependencies.IsEmpty);
        Assert.True(dto.RiskFactors.IsEmpty);
    }
}
