using Orkeon.Domain.Agent.Composition;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Composition;

/// <summary>
/// Tests for Composition Types following Clean Architecture principles.
/// Tests the various composition analysis and requirement records.
/// </summary>
public class CompositionTypesTests
{
    #region PredictedPerformance Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingPredictedPerformanceWithDefaultConstructor()
    {
        // Act
        var performance = new PredictedPerformance();

        // Assert
        Assert.Equal(0.0, performance.SuccessRate);
        Assert.Equal(0.0, performance.EfficiencyScore);
        Assert.Equal(0.0, performance.QualityScore);
        Assert.Equal(TimeSpan.Zero, performance.EstimatedDuration);
        Assert.NotNull(performance.MetricScores);
        Assert.Empty(performance.MetricScores);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingPredictedPerformanceProperties()
    {
        // Arrange
        var duration = TimeSpan.FromHours(8);
        var metricScores = new Dictionary<string, double>
        {
            { "accuracy", 0.95 },
            { "speed", 0.8 },
            { "reliability", 0.9 }
        };

        // Act
        var performance = new PredictedPerformance
        {
            SuccessRate = 0.85,
            EfficiencyScore = 0.75,
            QualityScore = 0.9,
            EstimatedDuration = duration,
            MetricScores = metricScores
        };

        // Assert
        Assert.Equal(0.85, performance.SuccessRate);
        Assert.Equal(0.75, performance.EfficiencyScore);
        Assert.Equal(0.9, performance.QualityScore);
        Assert.Equal(duration, performance.EstimatedDuration);
        Assert.Equal(metricScores, performance.MetricScores);
        Assert.Equal(3, performance.MetricScores.Count);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(-0.5)] // Out of range
    [InlineData(1.5)]  // Out of range
    public void ShouldAcceptAnyValue_WhenUsingPredictedPerformanceScoreValues(double score)
    {
        // Act
        var performance = new PredictedPerformance
        {
            SuccessRate = score,
            EfficiencyScore = score,
            QualityScore = score
        };

        // Assert
        Assert.Equal(score, performance.SuccessRate);
        Assert.Equal(score, performance.EfficiencyScore);
        Assert.Equal(score, performance.QualityScore);
    }

    [Fact]
    public void ShouldSupportDictionaryContent_WhenUsingPredictedPerformanceMetricScores()
    {
        // Arrange & Act
        var performance = new PredictedPerformance
        {
            MetricScores = new Dictionary<string, double>
            {
                { "latency", 0.05 },
                { "throughput", 0.95 }
            }
        };

        // Assert
        Assert.Equal(2, performance.MetricScores.Count);
        Assert.Equal(0.05, performance.MetricScores["latency"]);
        Assert.Equal(0.95, performance.MetricScores["throughput"]);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingPredictedPerformanceWithNegativeDuration()
    {
        // Act
        var performance = new PredictedPerformance
        {
            EstimatedDuration = TimeSpan.FromHours(-5)
        };

        // Assert
        Assert.Equal(TimeSpan.FromHours(-5), performance.EstimatedDuration);
    }

    #endregion

    #region SkillCoverageAnalysis Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingSkillCoverageAnalysisWithDefaultConstructor()
    {
        // Act
        var analysis = new SkillCoverageAnalysis();

        // Assert
        Assert.NotNull(analysis.RequiredSkillsCovered);
        Assert.Empty(analysis.RequiredSkillsCovered);
        Assert.NotNull(analysis.MissingSkills);
        Assert.Empty(analysis.MissingSkills);
        Assert.NotNull(analysis.RedundantSkills);
        Assert.Empty(analysis.RedundantSkills);
        Assert.Equal(0.0, analysis.CoveragePercentage);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingSkillCoverageAnalysisProperties()
    {
        // Arrange
        var requiredSkills = new Dictionary<string, bool>
        {
            { "Programming", true },
            { "Testing", true },
            { "DevOps", false },
            { "UI/UX", false }
        };
        var missingSkills = new List<string> { "DevOps", "UI/UX" };
        var redundantSkills = new List<string> { "Legacy Systems", "COBOL" };

        // Act
        var analysis = new SkillCoverageAnalysis
        {
            RequiredSkillsCovered = requiredSkills,
            MissingSkills = missingSkills,
            RedundantSkills = redundantSkills,
            CoveragePercentage = 0.5
        };

        // Assert
        Assert.Equal(requiredSkills, analysis.RequiredSkillsCovered);
        Assert.Equal(missingSkills, analysis.MissingSkills);
        Assert.Equal(redundantSkills, analysis.RedundantSkills);
        Assert.Equal(0.5, analysis.CoveragePercentage);
    }

    [Fact]
    public void ShouldScenario_WhenUsingSkillCoverageAnalysisCalculateCoverage()
    {
        // Arrange
        var skills = new[] { "C#", "JavaScript", "SQL", "Docker", "AWS" };
        var covered = new Dictionary<string, bool>();
        foreach (var skill in skills)
            covered[skill] = skill != "AWS";

        var coveredCount = covered.Count(kv => kv.Value);

        // Act
        var analysis = new SkillCoverageAnalysis
        {
            RequiredSkillsCovered = covered,
            MissingSkills = ["AWS"],
            RedundantSkills = ["VB6", "Flash"],
            CoveragePercentage = (double)coveredCount / skills.Length
        };

        // Assert
        Assert.Equal(5, analysis.RequiredSkillsCovered.Count);
        Assert.Single(analysis.MissingSkills);
        Assert.Equal(2, analysis.RedundantSkills.Count);
        Assert.Equal(0.8, analysis.CoveragePercentage);
    }

    #endregion

    #region RoleFulfillmentAnalysis Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingRoleFulfillmentAnalysisWithDefaultConstructor()
    {
        // Act
        var analysis = new RoleFulfillmentAnalysis();

        // Assert
        Assert.NotNull(analysis.RoleAssignments);
        Assert.Empty(analysis.RoleAssignments);
        Assert.NotNull(analysis.UnfilledRoles);
        Assert.Empty(analysis.UnfilledRoles);
        Assert.NotNull(analysis.OverstaffedRoles);
        Assert.Empty(analysis.OverstaffedRoles);
        Assert.Equal(0.0, analysis.FulfillmentScore);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRoleFulfillmentAnalysisProperties()
    {
        // Arrange
        var roleAssignments = new Dictionary<string, string>
        {
            { "Lead Developer", "agent-123" },
            { "QA Engineer", "agent-456" },
            { "DevOps", "agent-789" }
        };
        var unfilledRoles = new List<string> { "Product Owner", "Scrum Master" };
        var overstaffedRoles = new List<string> { "Junior Developer" };

        // Act
        var analysis = new RoleFulfillmentAnalysis
        {
            RoleAssignments = roleAssignments,
            UnfilledRoles = unfilledRoles,
            OverstaffedRoles = overstaffedRoles,
            FulfillmentScore = 0.75
        };

        // Assert
        Assert.Equal(roleAssignments, analysis.RoleAssignments);
        Assert.Equal(unfilledRoles, analysis.UnfilledRoles);
        Assert.Equal(overstaffedRoles, analysis.OverstaffedRoles);
        Assert.Equal(0.75, analysis.FulfillmentScore);
    }

    [Fact]
    public void ShouldCompleteScenario_WhenUsingRoleFulfillmentAnalysis()
    {
        // Act
        var analysis = new RoleFulfillmentAnalysis
        {
            RoleAssignments = new Dictionary<string, string>
            {
                { "Architect", "agent-001" },
                { RoleDeveloper, "agent-002" },
                { "Tester", "agent-003" },
                { "Junior Developer 1", "agent-004" },
                { "Junior Developer 2", "agent-005" },
                { "Junior Developer 3", "agent-006" }
            },
            UnfilledRoles = ["DevOps", RoleManager],
            OverstaffedRoles = ["Junior Developer"],
            FulfillmentScore = 3.0 / 5.0
        };

        // Assert
        Assert.Equal(6, analysis.RoleAssignments.Count);
        Assert.Equal(2, analysis.UnfilledRoles.Count);
        Assert.Single(analysis.OverstaffedRoles);
        Assert.Equal(0.6, analysis.FulfillmentScore);
    }

    #endregion

    #region CompositionGap Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingCompositionGapWithDefaultConstructor()
    {
        // Act
        var gap = new CompositionGap();

        // Assert
        Assert.Equal(string.Empty, gap.Type);
        Assert.Equal(string.Empty, gap.Name);
        Assert.Equal(string.Empty, gap.Description);
        Assert.Equal(0.0, gap.Severity);
        Assert.NotNull(gap.PotentialSolutions);
        Assert.Empty(gap.PotentialSolutions);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCompositionGapProperties()
    {
        // Arrange
        var solutions = new List<string>
        {
            "Hire a specialist",
            "Train existing team member",
            "Outsource the task"
        };

        // Act
        var gap = new CompositionGap
        {
            Type = "skill",
            Name = "Machine Learning Expertise",
            Description = "No team member has ML experience for the AI component",
            Severity = 0.8,
            PotentialSolutions = solutions
        };

        // Assert
        Assert.Equal("skill", gap.Type);
        Assert.Equal("Machine Learning Expertise", gap.Name);
        Assert.Equal("No team member has ML experience for the AI component", gap.Description);
        Assert.Equal(0.8, gap.Severity);
        Assert.Equal(solutions, gap.PotentialSolutions);
        Assert.Equal(3, gap.PotentialSolutions.Count);
    }

    [Theory]
    [InlineData("skill")]
    [InlineData("role")]
    [InlineData("tool")]
    [InlineData("experience")]
    [InlineData("unknown")]
    public void ShouldAcceptAnyString_WhenUsingCompositionGapTypeValues(string type)
    {
        // Act
        var gap = new CompositionGap { Type = type };

        // Assert
        Assert.Equal(type, gap.Type);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(-0.1)] // Out of expected range
    [InlineData(2.0)]  // Out of expected range
    public void ShouldAcceptAnyValue_WhenUsingCompositionGapSeverityValues(double severity)
    {
        // Act
        var gap = new CompositionGap { Severity = severity };

        // Assert
        Assert.Equal(severity, gap.Severity);
    }

    [Fact]
    public void ShouldSupportMultipleSolutions_WhenUsingCompositionGap()
    {
        // Act
        var gap = new CompositionGap
        {
            PotentialSolutions = ["Solution 1", "Solution 3", "Better Solution"]
        };

        // Assert
        Assert.Equal(3, gap.PotentialSolutions.Count);
        Assert.Contains("Solution 1", gap.PotentialSolutions);
        Assert.Contains("Better Solution", gap.PotentialSolutions);
    }

    #endregion

    #region RoleRequirement Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingRoleRequirementWithDefaultConstructor()
    {
        // Act
        var requirement = new RoleRequirement();

        // Assert
        Assert.Equal(string.Empty, requirement.RoleName);
        Assert.Equal(1, requirement.MinAgents);
        Assert.Equal(1, requirement.MaxAgents);
        Assert.NotNull(requirement.RequiredSkills);
        Assert.Empty(requirement.RequiredSkills);
        Assert.NotNull(requirement.RequiredTools);
        Assert.Empty(requirement.RequiredTools);
        Assert.True(requirement.IsCritical);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRoleRequirementProperties()
    {
        // Arrange
        var requiredSkills = new List<string> { "Python", "TensorFlow", "Data Analysis" };
        var requiredTools = new List<string> { "Jupyter", "Git", "Docker" };

        // Act
        var requirement = new RoleRequirement
        {
            RoleName = "Data Scientist",
            MinAgents = 2,
            MaxAgents = 4,
            RequiredSkills = requiredSkills,
            RequiredTools = requiredTools,
            IsCritical = false
        };

        // Assert
        Assert.Equal("Data Scientist", requirement.RoleName);
        Assert.Equal(2, requirement.MinAgents);
        Assert.Equal(4, requirement.MaxAgents);
        Assert.Equal(requiredSkills, requirement.RequiredSkills);
        Assert.Equal(requiredTools, requirement.RequiredTools);
        Assert.False(requirement.IsCritical);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 5)]
    [InlineData(0, 10)]
    [InlineData(-1, -1)] // Invalid but accepted
    [InlineData(5, 3)]   // Max < Min but accepted
    public void ShouldAcceptAnyValues_WhenUsingRoleRequirementAgentCounts(int min, int max)
    {
        // Act
        var requirement = new RoleRequirement { MinAgents = min, MaxAgents = max };

        // Assert
        Assert.Equal(min, requirement.MinAgents);
        Assert.Equal(max, requirement.MaxAgents);
    }

    [Fact]
    public void ShouldSupportCollections_WhenUsingRoleRequirement()
    {
        // Act
        var requirement = new RoleRequirement
        {
            RoleName = "Full Stack Developer",
            RequiredSkills = ["JavaScript", "Node.js", "React", "SQL"],
            RequiredTools = ["VS Code", "npm", "PostgreSQL"]
        };

        // Assert
        Assert.Equal(4, requirement.RequiredSkills.Count);
        Assert.Equal(3, requirement.RequiredTools.Count);
        Assert.Contains("React", requirement.RequiredSkills);
        Assert.Contains("PostgreSQL", requirement.RequiredTools);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldCompleteScenario_WhenUsingCompositionAnalysis()
    {
        // Arrange - Complete analysis of a development team composition
        var performance = new PredictedPerformance
        {
            SuccessRate = 0.82,
            EfficiencyScore = 0.75,
            QualityScore = 0.88,
            EstimatedDuration = TimeSpan.FromDays(90),
            MetricScores = new Dictionary<string, double>
            {
                { "code_quality", 0.9 },
                { "delivery_speed", 0.7 },
                { "team_collaboration", 0.85 }
            }
        };

        var skillCoverage = new SkillCoverageAnalysis
        {
            RequiredSkillsCovered = new Dictionary<string, bool>
            {
                { "Backend Development", true },
                { "Frontend Development", true },
                { "Database Design", true },
                { "Cloud Architecture", false },
                { "Security", false }
            },
            MissingSkills = ["Cloud Architecture", "Security"],
            RedundantSkills = ["Legacy Mainframe", "Flash Development"],
            CoveragePercentage = 0.6
        };

        var roleFulfillment = new RoleFulfillmentAnalysis
        {
            RoleAssignments = new Dictionary<string, string>
            {
                { "Tech Lead", "agent-TL1" },
                { "Backend Developer", "agent-BE1" },
                { "Frontend Developer", "agent-FE1" },
                { "QA Engineer", "agent-QA1" }
            },
            UnfilledRoles = ["DevOps Engineer", "Security Specialist"],
            OverstaffedRoles = [],
            FulfillmentScore = 0.67
        };

        var gaps = new List<CompositionGap>
        {
            new CompositionGap
            {
                Type = "skill",
                Name = "Cloud Architecture",
                Description = "No team member has AWS/Azure experience",
                Severity = 0.8,
                PotentialSolutions =
                [
                    "Hire AWS certified architect",
                    "Send backend developer for AWS training",
                    "Partner with cloud consulting firm"
                ]
            },
            new CompositionGap
            {
                Type = "role",
                Name = "DevOps Engineer",
                Description = "No dedicated DevOps role for CI/CD pipeline",
                Severity = 0.7,
                PotentialSolutions =
                [
                    "Hire DevOps engineer",
                    "Train existing developer in DevOps practices"
                ]
            }
        };

        // Assert - Verify complete analysis
        Assert.True(performance.SuccessRate > 0.8);
        Assert.Equal(TimeSpan.FromDays(90), performance.EstimatedDuration);

        Assert.Equal(5, skillCoverage.RequiredSkillsCovered.Count);
        Assert.Equal(2, skillCoverage.MissingSkills.Count);
        Assert.True(skillCoverage.CoveragePercentage < 0.7);

        Assert.Equal(4, roleFulfillment.RoleAssignments.Count);
        Assert.Equal(2, roleFulfillment.UnfilledRoles.Count);
        Assert.Empty(roleFulfillment.OverstaffedRoles);

        Assert.Equal(2, gaps.Count);
        Assert.All(gaps, g => Assert.True(g.Severity >= 0.7));
        Assert.All(gaps, g => Assert.NotEmpty(g.PotentialSolutions));
    }

    [Fact]
    public void ShouldScenario_WhenUsingRoleRequirementsTeamDefinition()
    {
        // Arrange - Define requirements for a complete software team
        var requirements = new List<RoleRequirement>
        {
            new RoleRequirement
            {
                RoleName = "Technical Architect",
                MinAgents = 1,
                MaxAgents = 1,
                RequiredSkills = ["System Design", "Cloud Architecture", "Security"],
                RequiredTools = ["Draw.io", "AWS", "Terraform"],
                IsCritical = true
            },
            new RoleRequirement
            {
                RoleName = "Backend Developer",
                MinAgents = 2,
                MaxAgents = 4,
                RequiredSkills = ["Java", "Spring Boot", "SQL", "REST APIs"],
                RequiredTools = ["IntelliJ IDEA", "Git", "Docker"],
                IsCritical = true
            },
            new RoleRequirement
            {
                RoleName = "Frontend Developer",
                MinAgents = 1,
                MaxAgents = 3,
                RequiredSkills = ["React", "TypeScript", "CSS", "Redux"],
                RequiredTools = ["VS Code", "npm", "Webpack"],
                IsCritical = true
            },
            new RoleRequirement
            {
                RoleName = "QA Automation Engineer",
                MinAgents = 1,
                MaxAgents = 2,
                RequiredSkills = ["Selenium", "Jest", "API Testing"],
                RequiredTools = ["Postman", "JMeter", "Cypress"],
                IsCritical = false
            }
        };

        // Act - Calculate minimum and maximum team size
        var minTeamSize = requirements.Sum(r => r.MinAgents);
        var maxTeamSize = requirements.Sum(r => r.MaxAgents);
        var criticalRoles = requirements.Where(r => r.IsCritical).Count();

        // Assert
        Assert.Equal(4, requirements.Count);
        Assert.Equal(5, minTeamSize);
        Assert.Equal(10, maxTeamSize);
        Assert.Equal(3, criticalRoles);
        Assert.All(requirements, r => Assert.NotEmpty(r.RequiredSkills));
        Assert.All(requirements, r => Assert.NotEmpty(r.RequiredTools));
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCompositionTypesWithNullCollections()
    {
        // Act
        var performance = new PredictedPerformance { MetricScores = null! };
        var skillCoverage = new SkillCoverageAnalysis
        {
            RequiredSkillsCovered = null!,
            MissingSkills = null!,
            RedundantSkills = null!
        };
        var roleFulfillment = new RoleFulfillmentAnalysis
        {
            RoleAssignments = null!,
            UnfilledRoles = null!,
            OverstaffedRoles = null!
        };
        var gap = new CompositionGap { PotentialSolutions = null! };
        var requirement = new RoleRequirement
        {
            RequiredSkills = null!,
            RequiredTools = null!
        };

        // Assert
        Assert.Null(performance.MetricScores);
        Assert.Null(skillCoverage.RequiredSkillsCovered);
        Assert.Null(skillCoverage.MissingSkills);
        Assert.Null(roleFulfillment.RoleAssignments);
        Assert.Null(gap.PotentialSolutions);
        Assert.Null(requirement.RequiredSkills);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCompositionTypesWithUnicodeContent()
    {
        // Act
        var gap = new CompositionGap
        {
            Type = "技能", // skill in Chinese
            Name = "Machine Learning 🤖",
            Description = "需要AI专家 (Need AI expert)",
            Severity = 0.9,
            PotentialSolutions =
            [
                "招聘专家 (Hire expert)",
                "培训团队 📚",
                "外包项目 🌐"
            ]
        };

        var requirement = new RoleRequirement
        {
            RoleName = "数据科学家 (Data Scientist)",
            RequiredSkills = ["Python 🐍", "机器学习", "データ分析"]
        };

        // Assert
        Assert.Contains("技能", gap.Type);
        Assert.Contains("🤖", gap.Name);
        Assert.Contains("📚", gap.PotentialSolutions[1]);
        Assert.Contains("🐍", requirement.RequiredSkills[0]);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingPredictedPerformanceExtremeValues()
    {
        // Act
        var performance = new PredictedPerformance
        {
            SuccessRate = double.MaxValue,
            EfficiencyScore = double.MinValue,
            QualityScore = double.NaN,
            EstimatedDuration = TimeSpan.MaxValue
        };

        // Assert
        Assert.Equal(double.MaxValue, performance.SuccessRate);
        Assert.Equal(double.MinValue, performance.EfficiencyScore);
        Assert.True(double.IsNaN(performance.QualityScore));
        Assert.Equal(TimeSpan.MaxValue, performance.EstimatedDuration);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingCompositionTypesToString()
    {
        // Arrange
        var performance = new PredictedPerformance { SuccessRate = 0.85 };
        var skillCoverage = new SkillCoverageAnalysis { CoveragePercentage = 0.75 };
        var roleFulfillment = new RoleFulfillmentAnalysis { FulfillmentScore = 0.9 };
        var gap = new CompositionGap { Name = "Test Gap" };
        var requirement = new RoleRequirement { RoleName = RoleDeveloper };

        // Act & Assert
        Assert.NotNull(performance.ToString());
        Assert.NotNull(skillCoverage.ToString());
        Assert.NotNull(roleFulfillment.ToString());
        Assert.NotNull(gap.ToString());
        Assert.NotNull(requirement.ToString());
    }

    #endregion
}
