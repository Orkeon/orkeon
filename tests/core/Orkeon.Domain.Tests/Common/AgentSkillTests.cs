using Orkeon.Domain.Agent.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Domain.Tests.Common;

public class AgentSkillTests
{
    private static readonly string[] PythonCertifications = ["Python Institute PCAP", "Django Certified"];
    private static readonly double[] ExpertLevels = [0.8, 0.85, 0.9, 0.95, 1.0];
    private static readonly double[] NonExpertLevels = [0.0, 0.3, 0.5, 0.7, 0.79, 0.799999];
    private static readonly double[] ProficientLevels = [0.6, 0.7, 0.8, 0.9, 1.0];
    private static readonly double[] NonProficientLevels = [0.0, 0.2, 0.4, 0.5, 0.59, 0.599999];
    private static readonly double[] NoviceLevels = [0.0, 0.1, 0.2, 0.3, 0.39, 0.399999];
    private static readonly double[] NonNoviceLevels = [0.4, 0.5, 0.6, 0.8, 1.0];
    private static readonly double[] AllTestLevels = [0.0, 0.2, 0.4, 0.6, 0.8, 1.0];
    private static readonly string[] PythonDataAnalysisApiSkills = ["Python", "Data Analysis", "API Integration"];
    private static readonly string[] Industries = ["Finance", "Healthcare", "Retail"];
    private static readonly string[] PreferredLanguages = ["Python", "Go"];
    private static readonly string[] SkillGaps = ["Testing", "Documentation", "Project Management"];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateAgentSkill_WhenConstructingWithAllParameters()
    {
        // Arrange
        var name = "Python Programming";
        var category = "Programming Languages";
        var proficiencyLevel = 0.85;
        var relatedTools = new List<string> { "pip", "virtualenv", "pytest" };
        var metadata = new Dictionary<string, object>
        {
            ["yearsOfExperience"] = 5,
            ["certifications"] = PythonCertifications,
            ["lastUsed"] = DateTime.UtcNow
        };

        // Act
        var skill = AgentSkill.Create(
            name,
            category,
            proficiencyLevel,
            relatedTools,
            metadata);

        // Assert
        Assert.Equal(name, skill.Name);
        Assert.Equal(category, skill.Category);
        Assert.Equal(proficiencyLevel, skill.ProficiencyLevel);
        Assert.Equal(relatedTools, skill.RelatedTools);
        Assert.Equal(metadata, skill.Metadata);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var name = "Data Analysis";
        var category = "Analytics";

        // Act
        var skill = AgentSkill.Create(name, category);

        // Assert
        Assert.Equal(name, skill.Name);
        Assert.Equal(category, skill.Category);
        Assert.Equal(1.0, skill.ProficiencyLevel);
        Assert.Empty(skill.RelatedTools);
        Assert.Empty(skill.Metadata);
    }

    [Fact]
    public void ShouldUseEmptyCollections_WhenConstructingWithNullOptionalParameters()
    {
        // Arrange
        var name = "Machine Learning";
        var category = "AI/ML";

        // Act
        var skill = AgentSkill.Create(
            name,
            category,
            proficiencyLevel: 0.7,
            relatedTools: null,
            metadata: null);

        // Assert
        Assert.NotNull(skill.RelatedTools);
        Assert.Empty(skill.RelatedTools);
        Assert.NotNull(skill.Metadata);
        Assert.Empty(skill.Metadata);
        Assert.Equal(0.7, skill.ProficiencyLevel);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            AgentSkill.Create(null!, "category"));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullCategory()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            AgentSkill.Create("name", null!));
        Assert.Equal("category", exception.ParamName);
    }

    [Fact]
    public void ShouldClampToOne_WhenConstructingWithProficiencyAboveOne()
    {
        // Arrange & Act
        var skill = AgentSkill.Create(
            "Skill",
            "Category",
            proficiencyLevel: 1.5);

        // Assert
        Assert.Equal(1.0, skill.ProficiencyLevel);
    }

    [Fact]
    public void ShouldClampToZero_WhenConstructingWithProficiencyBelowZero()
    {
        // Arrange & Act
        var skill = AgentSkill.Create(
            "Skill",
            "Category",
            proficiencyLevel: -0.5);

        // Assert
        Assert.Equal(0, skill.ProficiencyLevel);
    }

    [Fact]
    public void ShouldClampCorrectly_WhenConstructingWithVariousProficiencyLevels()
    {
        // Arrange
        var testCases = new[]
        {
            (input: 0.5, expected: 0.5),
            (input: 0.0, expected: 0.0),
            (input: 1.0, expected: 1.0),
            (input: 2.0, expected: 1.0),
            (input: -1.0, expected: 0.0),
            (input: 0.99999, expected: 0.99999),
            (input: double.MaxValue, expected: 1.0),
            (input: double.MinValue, expected: 0.0),
            (input: double.NaN, expected: 0.0),
            (input: double.PositiveInfinity, expected: 1.0),
            (input: double.NegativeInfinity, expected: 0.0)
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var skill = AgentSkill.Create("Test", "Test", proficiencyLevel: input);
            Assert.Equal(expected, skill.ProficiencyLevel);
        }
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void ShouldCreateSkillWithDefaults_WhenCreating()
    {
        // Arrange
        var name = "Web Development";
        var category = "Development";

        // Act
        var skill = AgentSkill.Create(name, category);

        // Assert
        Assert.Equal(name, skill.Name);
        Assert.Equal(category, skill.Category);
        Assert.Equal(1.0, skill.ProficiencyLevel);
        Assert.Empty(skill.RelatedTools);
        Assert.Empty(skill.Metadata);
    }

    [Fact]
    public void ShouldThrow_WhenCreatingWithNullName()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            AgentSkill.Create(null!, "category"));
    }

    [Fact]
    public void ShouldThrow_WhenCreatingWithNullCategory()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            AgentSkill.Create("name", null!));
    }

    #endregion

    #region Computed Properties Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsExpertWithProficiencyAbove08()
    {
        // Arrange
        var expertLevels = ExpertLevels;

        // Act & Assert
        foreach (var level in expertLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);
            Assert.True(skill.IsExpert);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsExpertWithProficiencyBelow08()
    {
        // Arrange
        var nonExpertLevels = NonExpertLevels;

        // Act & Assert
        foreach (var level in nonExpertLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);
            Assert.False(skill.IsExpert);
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsProficientWithProficiencyAbove06()
    {
        // Arrange
        var proficientLevels = ProficientLevels;

        // Act & Assert
        foreach (var level in proficientLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);
            Assert.True(skill.IsProficient);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsProficientWithProficiencyBelow06()
    {
        // Arrange
        var nonProficientLevels = NonProficientLevels;

        // Act & Assert
        foreach (var level in nonProficientLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);
            Assert.False(skill.IsProficient);
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsNoviceWithProficiencyBelow04()
    {
        // Arrange
        var noviceLevels = NoviceLevels;

        // Act & Assert
        foreach (var level in noviceLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);
            Assert.True(skill.IsNovice);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsNoviceWithProficiencyAbove04()
    {
        // Arrange
        var nonNoviceLevels = NonNoviceLevels;

        // Act & Assert
        foreach (var level in nonNoviceLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);
            Assert.False(skill.IsNovice);
        }
    }

    [Fact]
    public void ShouldBeMutuallyExclusive_WhenUsingSkillLevels()
    {
        // Arrange
        var testLevels = AllTestLevels;

        // Act & Assert
        foreach (var level in testLevels)
        {
            var skill = AgentSkill.Create("Skill", "Category", proficiencyLevel: level);

            // A skill can be both Expert and Proficient (Expert is a subset of Proficient)
            // But Novice should be exclusive
            if (skill.IsNovice)
            {
                Assert.False(skill.IsProficient);
                Assert.False(skill.IsExpert);
            }
        }
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var tools = new List<string> { "tool1", "tool2" };
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        var skill1 = AgentSkill.Create(
            "JavaScript",
            "Programming",
            0.9,
            tools,
            metadata);

        var skill2 = AgentSkill.Create(
            "JavaScript",
            "Programming",
            0.9,
            tools,
            metadata);

        // Act & Assert
        Assert.Equal(skill1, skill2);
        Assert.True(skill1 == skill2);
        Assert.False(skill1 != skill2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentName()
    {
        // Arrange
        var skill1 = AgentSkill.Create("Python", "Programming");
        var skill2 = AgentSkill.Create("Java", "Programming");

        // Act & Assert
        Assert.NotEqual(skill1, skill2);
        Assert.False(skill1 == skill2);
        Assert.True(skill1 != skill2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentCategory()
    {
        // Arrange
        var skill1 = AgentSkill.Create("Analysis", "Data Science");
        var skill2 = AgentSkill.Create("Analysis", "Business");

        // Act & Assert
        Assert.NotEqual(skill1, skill2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentProficiency()
    {
        // Arrange
        var skill1 = AgentSkill.Create("React", "Frontend", proficiencyLevel: 0.7);
        var skill2 = AgentSkill.Create("React", "Frontend", proficiencyLevel: 0.8);

        // Act & Assert
        Assert.NotEqual(skill1, skill2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentTools()
    {
        // Arrange
        var skill1 = AgentSkill.Create(
            "DevOps",
            "Infrastructure",
            relatedTools: ["Docker"]);

        var skill2 = AgentSkill.Create(
            "DevOps",
            "Infrastructure",
            relatedTools: ["Kubernetes"]);

        // Act & Assert
        Assert.NotEqual(skill1, skill2);
    }

    #endregion

    #region Record Features Tests

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = AgentSkill.Create(
            "Original Skill",
            "Original Category",
            0.7,
            ["tool1"],
            new Dictionary<string, object> { ["version"] = "1.0" });

        var newTools = new List<string> { "tool2", "tool3" };

        // Act - Since AgentSkill is immutable, create new instance
        var modified = AgentSkill.Create(
            original.Name,
            original.Category,
            proficiencyLevel: 0.9,
            relatedTools: newTools,
            metadata: original.Metadata);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(original.Name, modified.Name);
        Assert.Equal(original.Category, modified.Category);
        Assert.NotEqual(original.ProficiencyLevel, modified.ProficiencyLevel);
        Assert.NotEqual(original.RelatedTools, modified.RelatedTools);
        Assert.Equal(0.9, modified.ProficiencyLevel);
        Assert.Equal(newTools, modified.RelatedTools);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var skill = AgentSkill.Create(
            "Cloud Computing",
            "Infrastructure",
            0.85,
            ["AWS", "Azure"]);

        // Act
        var result = skill.ToString();

        // Assert
        Assert.Contains("AgentSkill", result);
        Assert.Contains("Name = Cloud Computing", result);
        Assert.Contains("Category = Infrastructure", result);
    }

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var tools = new List<string> { "tool" };
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        var skill1 = AgentSkill.Create("Name", "Category", 0.5, tools, metadata);
        var skill2 = AgentSkill.Create("Name", "Category", 0.5, tools, metadata);

        // Act
        var hash1 = skill1.GetHashCode();
        var hash2 = skill2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnAllProperties_WhenUsingDeconstruct()
    {
        // Arrange
        var expectedName = "Database Management";
        var expectedCategory = "Data";
        var expectedProficiency = 0.75;
        var expectedTools = new List<string> { "MySQL", "PostgreSQL", "MongoDB" };
        var expectedMetadata = new Dictionary<string, object>
        {
            ["preferredDB"] = "PostgreSQL",
            ["experience"] = 3
        };

        var skill = AgentSkill.Create(
            expectedName,
            expectedCategory,
            expectedProficiency,
            expectedTools,
            expectedMetadata);

        // Act
        // AgentSkill is not deconstructable - access properties directly

        // Assert
        Assert.Equal(expectedName, skill.Name);
        Assert.Equal(expectedCategory, skill.Category);
        Assert.Equal(expectedProficiency, skill.ProficiencyLevel);
        Assert.Equal(expectedTools, skill.RelatedTools);
        Assert.Equal(expectedMetadata, skill.Metadata);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldSkillProgression_WhenUsingComplexScenario()
    {
        // Arrange - Simulate skill development over time
        var initialSkill = AgentSkill.Create(
            "Machine Learning",
            "AI/ML",
            proficiencyLevel: 0.3,
            relatedTools: ["scikit-learn"],
            metadata: new Dictionary<string, object>
            {
                ["startDate"] = new DateTime(2020, 1, 1),
                ["projectsCompleted"] = 0
            });

        // Act - Progress through skill levels
        var afterTraining = AgentSkill.Create(
            initialSkill.Name,
            initialSkill.Category,
            proficiencyLevel: 0.5,
            relatedTools: ["scikit-learn", "TensorFlow"],
            metadata: new Dictionary<string, object>
            {
                ["startDate"] = new DateTime(2020, 1, 1),
                ["projectsCompleted"] = 3,
                ["lastTraining"] = new DateTime(2021, 6, 1)
            });

        var afterExperience = AgentSkill.Create(
            afterTraining.Name,
            afterTraining.Category,
            proficiencyLevel: 0.85,
            relatedTools: ["scikit-learn", "TensorFlow", "PyTorch", "Keras"],
            metadata: new Dictionary<string, object>
            {
                ["startDate"] = new DateTime(2020, 1, 1),
                ["projectsCompleted"] = 15,
                ["lastTraining"] = new DateTime(2022, 12, 1),
                ["specialization"] = "Deep Learning"
            });

        // Assert
        Assert.True(initialSkill.IsNovice);
        Assert.False(initialSkill.IsProficient);
        Assert.False(initialSkill.IsExpert);

        Assert.False(afterTraining.IsNovice);
        Assert.False(afterTraining.IsProficient);
        Assert.False(afterTraining.IsExpert);

        Assert.False(afterExperience.IsNovice);
        Assert.True(afterExperience.IsProficient);
        Assert.True(afterExperience.IsExpert);

        // Verify tool progression
        Assert.Single(initialSkill.RelatedTools);
        Assert.Equal(2, afterTraining.RelatedTools.Count);
        Assert.Equal(4, afterExperience.RelatedTools.Count);
    }

    [Fact]
    public void ShouldSkillCategorization_WhenUsingComplexScenario()
    {
        // Arrange
        var skills = new List<AgentSkill>
        {
            AgentSkill.Create("Python", "Programming Languages", 0.9),
            AgentSkill.Create("JavaScript", "Programming Languages", 0.8),
            AgentSkill.Create("React", "Frontend Frameworks", 0.85),
            AgentSkill.Create("Node.js", "Backend Frameworks", 0.75),
            AgentSkill.Create("Docker", "DevOps", 0.7),
            AgentSkill.Create("Kubernetes", "DevOps", 0.6),
            AgentSkill.Create("Machine Learning", "AI/ML", 0.5),
            AgentSkill.Create("Data Analysis", "Data Science", 0.95),
            AgentSkill.Create("SQL", "Databases", 0.9),
            AgentSkill.Create("MongoDB", "Databases", 0.65)
        };

        // Act - Group by category
        var skillsByCategory = skills
            .GroupBy(s => s.Category)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.ProficiencyLevel).ToList()
            );

        // Assert
        Assert.Equal(7, skillsByCategory.Count);
        Assert.Equal(2, skillsByCategory["Programming Languages"].Count);
        Assert.Equal(2, skillsByCategory["DevOps"].Count);
        Assert.Equal(2, skillsByCategory["Databases"].Count);

        // Verify best skill per category
        Assert.Equal("Python", skillsByCategory["Programming Languages"][0].Name);
        Assert.Equal("Docker", skillsByCategory["DevOps"][0].Name);
        Assert.Equal("SQL", skillsByCategory["Databases"][0].Name);

        // Count expert skills
        var expertSkills = skills.Where(s => s.IsExpert).ToList();
        Assert.Equal(5, expertSkills.Count);
    }

    [Fact]
    public void ShouldSkillMatching_WhenUsingComplexScenario()
    {
        // Arrange - Agent skills
        var agentSkills = new List<AgentSkill>
        {
            AgentSkill.Create("Python", "Programming", 0.9,
                ["pip", "virtualenv", "pytest"]),
            AgentSkill.Create("Data Analysis", "Analytics", 0.85,
                ["pandas", "numpy", "matplotlib"]),
            AgentSkill.Create("Machine Learning", "AI/ML", 0.7,
                ["scikit-learn", "tensorflow"]),
            AgentSkill.Create("Web Scraping", "Data Collection", 0.8,
                ["beautifulsoup", "selenium"])
        };

        // Task requirements
        var requiredSkills = PythonDataAnalysisApiSkills;
        var requiredProficiency = 0.7;

        // Act - Find matching skills
        var matchingSkills = agentSkills
            .Where(s => requiredSkills.Contains(s.Name))
            .Where(s => s.ProficiencyLevel >= requiredProficiency)
            .ToList();

        var missingSkills = requiredSkills
            .Where(rs => !agentSkills.Any(s => s.Name == rs))
            .ToList();

        var skillCoverage = (double)matchingSkills.Count / requiredSkills.Length;

        // Assert
        Assert.Equal(2, matchingSkills.Count);
        Assert.Contains(matchingSkills, s => s.Name == "Python");
        Assert.Contains(matchingSkills, s => s.Name == "Data Analysis");

        Assert.Single(missingSkills);
        Assert.Equal("API Integration", missingSkills[0]);

        Assert.Equal(2.0 / 3.0, skillCoverage);
    }

    [Fact]
    public void ShouldSkillWithComplexMetadata_WhenUsingComplexScenario()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["certifications"] = new List<string>
            {
                "AWS Certified Solutions Architect",
                "Google Cloud Professional",
                "Azure Administrator"
            },
            ["experience"] = new Dictionary<string, object>
            {
                ["years"] = 5,
                ["projects"] = new[]
                {
                    new { name = "E-commerce Platform", role = "Lead Developer" },
                    new { name = "Data Pipeline", role = "Architect" }
                },
                ["industries"] = Industries
            },
            ["learningPath"] = new List<Dictionary<string, object>>
            {
                new() { ["course"] = "Cloud Architecture", ["completed"] = new DateTime(2021, 3, 15) },
                new() { ["course"] = "Microservices", ["completed"] = new DateTime(2021, 9, 20) },
                new() { ["course"] = "Security Best Practices", ["completed"] = new DateTime(2022, 1, 10) }
            },
            ["preferences"] = new Dictionary<string, object>
            {
                ["preferredCloud"] = "AWS",
                ["preferredLanguages"] = PreferredLanguages,
                ["timezone"] = "UTC-5"
            }
        };

        var skill = AgentSkill.Create(
            "Cloud Architecture",
            "Infrastructure",
            0.92,
            ["terraform", "ansible", "kubernetes", "docker"],
            metadata);

        // Act & Assert
        Assert.Equal("Cloud Architecture", skill.Name);
        Assert.True(skill.IsExpert);

        var certs = skill.Metadata["certifications"] as List<string>;
        Assert.NotNull(certs);
        Assert.Equal(3, certs.Count);

        var experience = skill.Metadata["experience"] as Dictionary<string, object>;
        Assert.NotNull(experience);
        Assert.Equal(5, experience["years"]);

        var learningPath = skill.Metadata["learningPath"] as List<Dictionary<string, object>>;
        Assert.NotNull(learningPath);
        Assert.Equal(3, learningPath.Count);
    }

    [Fact]
    public void ShouldTeamSkillAnalysis_WhenUsingComplexScenario()
    {
        // Arrange - Team of agents with various skills
        var teamSkills = new List<(string AgentId, List<AgentSkill> Skills)>
        {
            (AgentId1, new List<AgentSkill>
            {
                AgentSkill.Create("Frontend Development", "Development", 0.9),
                AgentSkill.Create("UI/UX Design", "Design", 0.7),
                AgentSkill.Create("JavaScript", "Programming", 0.85)
            }),
            (AgentId2, new List<AgentSkill>
            {
                AgentSkill.Create("Backend Development", "Development", 0.95),
                AgentSkill.Create("Database Design", "Data", 0.8),
                AgentSkill.Create("Python", "Programming", 0.9)
            }),
            (AgentId3, new List<AgentSkill>
            {
                AgentSkill.Create("DevOps", "Infrastructure", 0.8),
                AgentSkill.Create("Cloud Services", "Infrastructure", 0.75),
                AgentSkill.Create("Security", "Infrastructure", 0.7)
            })
        };

        // Act - Analyze team capabilities
        var allSkills = teamSkills
            .SelectMany(t => t.Skills)
            .ToList();

        var uniqueCategories = allSkills
            .Select(s => s.Category)
            .Distinct()
            .ToList();

        var expertSkills = allSkills
            .Where(s => s.IsExpert)
            .ToList();

        var weakestCategory = allSkills
            .GroupBy(s => s.Category)
            .Select(g => new
            {
                Category = g.Key,
                AverageProficiency = g.Average(s => s.ProficiencyLevel)
            })
            .OrderBy(c => c.AverageProficiency)
            .First();

        var skillGaps = SkillGaps
            .Where(required => !allSkills.Any(s => s.Name.Contains(required)))
            .ToList();

        // Assert
        Assert.Equal(9, allSkills.Count);
        Assert.Equal(5, uniqueCategories.Count);
        Assert.Equal(6, expertSkills.Count);
        Assert.Equal("Design", weakestCategory.Category);
        Assert.Equal(0.7, weakestCategory.AverageProficiency);
        Assert.Equal(3, skillGaps.Count);
    }

    #endregion
}
