using Orkeon.Application.Training;

namespace Orkeon.Application.Tests.Common;

public class SkillDevelopmentTests
{
    [Fact]
    public void ShouldSetAllValues_WhenConstructingWithAllParameters()
    {
        // Arrange
        var skillName = "Machine Learning";
        var currentLevel = 3.5;
        var targetLevel = 8.0;
        var improvementRate = 0.5;
        var practiceHours = 120;
        var completedExercises = new List<string> { "Linear Regression", "Decision Trees" };

        // Act
        var skill = new SkillDevelopment(
            skillName,
            currentLevel,
            targetLevel,
            improvementRate,
            practiceHours,
            completedExercises);

        // Assert
        Assert.Equal("Machine Learning", skill.SkillName);
        Assert.Equal(3.5, skill.CurrentLevel);
        Assert.Equal(8.0, skill.TargetLevel);
        Assert.Equal(0.5, skill.ImprovementRate);
        Assert.Equal(120, skill.PracticeHours);
        Assert.Equal(2, skill.CompletedExercises.Count);
        Assert.Contains("Linear Regression", skill.CompletedExercises);
        Assert.Contains("Decision Trees", skill.CompletedExercises);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingProgressPercentageWithValidLevels()
    {
        // Arrange & Act
        var skill1 = new SkillDevelopment(
            "Python",
            5.0,
            10.0,
            0.2,
            50,
            []);

        var skill2 = new SkillDevelopment(
            "JavaScript",
            7.5,
            10.0,
            0.3,
            80,
            []);

        var skill3 = new SkillDevelopment(
            "C++",
            2.0,
            8.0,
            0.1,
            20,
            []);

        // Assert
        Assert.Equal(50.0, skill1.ProgressPercentage);
        Assert.Equal(75.0, skill2.ProgressPercentage);
        Assert.Equal(25.0, skill3.ProgressPercentage);
    }

    [Fact]
    public void ShouldCapAt100_WhenUsingProgressPercentageWhenCurrentExceedsTarget()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "Expert Skill",
            12.0,
            10.0,
            0.8,
            200,
            []);

        // Act & Assert
        Assert.Equal(100.0, skill.ProgressPercentage);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingProgressPercentageWithZeroTarget()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "New Skill",
            5.0,
            0.0,
            0.1,
            10,
            []);

        // Act & Assert
        Assert.Equal(0.0, skill.ProgressPercentage);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingProgressPercentageWithNegativeTarget()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "Invalid Skill",
            5.0,
            -10.0,
            0.1,
            10,
            []);

        // Act & Assert
        Assert.Equal(0.0, skill.ProgressPercentage);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsTargetAchievedWhenCurrentEqualsTarget()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "Completed Skill",
            10.0,
            10.0,
            0.5,
            100,
            []);

        // Act & Assert
        Assert.True(skill.IsTargetAchieved);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsTargetAchievedWhenCurrentExceedsTarget()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "Mastered Skill",
            12.0,
            10.0,
            0.6,
            150,
            []);

        // Act & Assert
        Assert.True(skill.IsTargetAchieved);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTargetAchievedWhenCurrentBelowTarget()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "In Progress Skill",
            7.5,
            10.0,
            0.3,
            75,
            []);

        // Act & Assert
        Assert.False(skill.IsTargetAchieved);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var exercises = new List<string> { "Exercise 1", "Exercise 2" };
        var skill1 = new SkillDevelopment(
            "Data Science",
            6.0,
            10.0,
            0.4,
            80,
            exercises);
        var skill2 = new SkillDevelopment(
            "Data Science",
            6.0,
            10.0,
            0.4,
            80,
            exercises);

        // Act & Assert
        Assert.Equal(skill1, skill2);
        Assert.True(skill1 == skill2);
        Assert.False(skill1 != skill2);
        Assert.Equal(skill1.GetHashCode(), skill2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var baseSkill = new SkillDevelopment(
            "Base Skill",
            5.0,
            10.0,
            0.3,
            50,
            ["Exercise 1"]);

        // Act & Assert - Different SkillName
        var differentName = new SkillDevelopment(
            "Different Skill",
            5.0,
            10.0,
            0.3,
            50,
            ["Exercise 1"]);
        Assert.NotEqual(baseSkill, differentName);

        // Different CurrentLevel
        var differentCurrent = new SkillDevelopment(
            "Base Skill",
            6.0,
            10.0,
            0.3,
            50,
            ["Exercise 1"]);
        Assert.NotEqual(baseSkill, differentCurrent);

        // Different CompletedExercises
        var differentExercises = new SkillDevelopment(
            "Base Skill",
            5.0,
            10.0,
            0.3,
            50,
            ["Exercise 2"]);
        Assert.NotEqual(baseSkill, differentExercises);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithSyntax()
    {
        // Arrange
        var original = new SkillDevelopment(
            "React",
            4.0,
            8.0,
            0.25,
            40,
            ["Components", "Hooks"]);

        // Act
        var modified = original with
        {
            CurrentLevel = 5.5,
            PracticeHours = 60,
            CompletedExercises = ["Components", "Hooks", "Redux"]
        };

        // Assert
        Assert.NotEqual(original, modified);
        Assert.Equal(4.0, original.CurrentLevel);
        Assert.Equal(5.5, modified.CurrentLevel);
        Assert.Equal(40, original.PracticeHours);
        Assert.Equal(60, modified.PracticeHours);
        Assert.Equal(2, original.CompletedExercises.Count);
        Assert.Equal(3, modified.CompletedExercises.Count);
        // Unchanged values
        Assert.Equal(original.SkillName, modified.SkillName);
        Assert.Equal(original.TargetLevel, modified.TargetLevel);
        Assert.Equal(original.ImprovementRate, modified.ImprovementRate);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        var exercises = new List<string> { "Test 1", "Test 2" };
        var skill = new SkillDevelopment(
            "Testing",
            7.0,
            10.0,
            0.35,
            70,
            exercises);

        // Act
        var (skillName, currentLevel, targetLevel, improvementRate, practiceHours, completedExercises) = skill;

        // Assert
        Assert.Equal("Testing", skillName);
        Assert.Equal(7.0, currentLevel);
        Assert.Equal(10.0, targetLevel);
        Assert.Equal(0.35, improvementRate);
        Assert.Equal(70, practiceHours);
        Assert.Equal(exercises, completedExercises);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var skill = new SkillDevelopment(
            "Docker",
            6.5,
            9.0,
            0.45,
            65,
            ["Containers", "Compose"]);

        // Act
        var result = skill.ToString();

        // Assert
        Assert.Contains("Docker", result);
        Assert.Contains("6.5", result);
        Assert.Contains("9", result);
        Assert.Contains("0.45", result);
        Assert.Contains("65", result);
    }

    [Fact]
    public void ShouldAccept_WhenUsingBoundaryValues()
    {
        // Act & Assert - Zero current level
        var zeroLevel = new SkillDevelopment(
            "New Skill",
            0.0,
            10.0,
            0.1,
            0,
            []);
        Assert.Equal(0.0, zeroLevel.CurrentLevel);
        Assert.Equal(0.0, zeroLevel.ProgressPercentage);

        // Negative improvement rate (skill degradation?)
        var negativeRate = new SkillDevelopment(
            "Rusty Skill",
            5.0,
            10.0,
            -0.1,
            10,
            []);
        Assert.Equal(-0.1, negativeRate.ImprovementRate);

        // Very high values
        var highValues = new SkillDevelopment(
            "Expert Skill",
            999.9,
            1000.0,
            5.0,
            10000,
            []);
        Assert.Equal(999.9, highValues.CurrentLevel);
        Assert.Equal(99.99, highValues.ProgressPercentage, 2);
    }

    [Fact]
    public void ShouldSkillProgressionPath_WhenUsingComplexScenario()
    {
        // Arrange - Simulate a learning progression
        var beginnerSkill = new SkillDevelopment(
            "Cloud Computing",
            1.0,
            10.0,
            0.2,
            10,
            ["Introduction to Cloud"]);

        var intermediateSkill = beginnerSkill with
        {
            CurrentLevel = 5.0,
            PracticeHours = 100,
            CompletedExercises =
            [
                "Introduction to Cloud",
                "AWS Fundamentals",
                "Azure Basics",
                "GCP Overview",
                "Containerization"
            ]
        };

        var advancedSkill = intermediateSkill with
        {
            CurrentLevel = 8.5,
            ImprovementRate = 0.4, // Faster improvement with experience
            PracticeHours = 250,
            CompletedExercises =
            [
                "Introduction to Cloud",
                "AWS Fundamentals",
                "Azure Basics",
                "GCP Overview",
                "Containerization",
                "Kubernetes",
                "Serverless Architecture",
                "Cloud Security",
                "Multi-Cloud Strategies"
            ]
        };

        // Act & Assert - Verify progression
        Assert.Equal(10.0, beginnerSkill.ProgressPercentage, 2); // 1.0/10.0 * 100 = 10%
        Assert.False(beginnerSkill.IsTargetAchieved);
        Assert.Single(beginnerSkill.CompletedExercises);

        Assert.Equal(50.0, intermediateSkill.ProgressPercentage);
        Assert.False(intermediateSkill.IsTargetAchieved);
        Assert.Equal(5, intermediateSkill.CompletedExercises.Count);

        Assert.Equal(85.0, advancedSkill.ProgressPercentage);
        Assert.False(advancedSkill.IsTargetAchieved);
        Assert.Equal(9, advancedSkill.CompletedExercises.Count);
        Assert.Equal(0.4, advancedSkill.ImprovementRate);

        // Simulate reaching target
        var masteredSkill = advancedSkill with { CurrentLevel = 10.0 };
        Assert.True(masteredSkill.IsTargetAchieved);
        Assert.Equal(100.0, masteredSkill.ProgressPercentage);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingEmptyExercisesList()
    {
        // Arrange & Act
        var skill = new SkillDevelopment(
            "Brand New Skill",
            0.0,
            5.0,
            0.1,
            0,
            []);

        // Assert
        Assert.NotNull(skill.CompletedExercises);
        Assert.Empty(skill.CompletedExercises);
    }

    [Fact]
    public void ShouldBeMutable_WhenUsingExercisesList()
    {
        // Arrange
        var exercises = new List<string> { "Exercise 1" };
        var skill = new SkillDevelopment(
            "Mutable Skill",
            3.0,
            6.0,
            0.2,
            30,
            exercises);

        // Act - Modify the original list
        exercises.Add("Exercise 2");

        // Assert - The skill's list should also be modified (reference type)
        Assert.Equal(2, skill.CompletedExercises.Count);
        Assert.Contains("Exercise 2", skill.CompletedExercises);
    }

    [Fact]
    public void ShouldMultipleSkillsComparison()
    {
        // Arrange - Create a portfolio of skills
        var skills = new List<SkillDevelopment>
        {
            new("Python", 8.5, 10.0, 0.6, 500, ["Django", "FastAPI", "Pandas"]),
            new("JavaScript", 7.0, 10.0, 0.5, 400, ["React", "Node.js"]),
            new("Go", 3.0, 8.0, 0.3, 100, ["Basics"]),
            new("Rust", 1.5, 7.0, 0.2, 50, []),
            new("SQL", 9.0, 9.0, 0.8, 300, ["Advanced Queries", "Optimization"])
        };

        // Act - Find skills by different criteria
        var achievedSkills = skills.Where(s => s.IsTargetAchieved).ToList();
        var highProgressSkills = skills.Where(s => s.ProgressPercentage >= 70).ToList();
        var mostPracticedSkill = skills.OrderByDescending(s => s.PracticeHours).First();
        var fastestLearningSkill = skills.OrderByDescending(s => s.ImprovementRate).First();

        // Assert
        Assert.Single(achievedSkills);
        Assert.Equal("SQL", achievedSkills[0].SkillName);

        Assert.Equal(3, highProgressSkills.Count);
        Assert.Contains(highProgressSkills, s => s.SkillName == "Python");
        Assert.Contains(highProgressSkills, s => s.SkillName == "JavaScript");
        Assert.Contains(highProgressSkills, s => s.SkillName == "SQL");

        Assert.Equal("Python", mostPracticedSkill.SkillName);
        Assert.Equal("SQL", fastestLearningSkill.SkillName);
    }
}
