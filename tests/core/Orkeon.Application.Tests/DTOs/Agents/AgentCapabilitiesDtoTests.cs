using Orkeon.Application.Agent.DTOs;
using System.Collections.Immutable;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Application.Tests.DTOs.Agents;

public class AgentCapabilitiesDtoTests
{
    private static readonly string[] s_skills3 = ["C#", "Python", "JavaScript"];
    private static readonly string[] s_skills2 = ["C#", "Python"];
    private static readonly string[] s_skillsDuplicate = ["C#", "C#", "Python"];
    private static readonly string[] s_tools3 = ["FileReadTool", "WebScrapeTool", ToolSearch];
    private static readonly string[] s_tools2 = ["FileReadTool", ToolSearch];
    private static readonly string[] s_languages3 = ["English", "French", "Spanish"];
    private static readonly string[] s_languages2 = ["English", "French"];
    private static readonly string[] s_specializations3 = ["Machine Learning", "Web Development", "DevOps"];
    private static readonly string[] s_specializations2 = ["AI", "Web Development"];

    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var dto = new AgentCapabilitiesDto();

        // Assert
        Assert.True(dto.Skills.IsEmpty);
        Assert.True(dto.Tools.IsEmpty);
        Assert.True(dto.Languages.IsEmpty);
        Assert.Equal("Medium", dto.OverallConfidence);
        Assert.True(dto.Specializations.IsEmpty);
        Assert.Equal("Standard", dto.CertificationLevel);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingSkills()
    {
        // Arrange
        var skills = ImmutableList<string>.Empty.AddRange(s_skills3);

        // Act
        var dto = new AgentCapabilitiesDto
        {
            Skills = skills
        };

        // Assert
        Assert.Equal(3, dto.Skills.Count);
        Assert.Contains("C#", dto.Skills);
        Assert.Contains("Python", dto.Skills);
        Assert.Contains("JavaScript", dto.Skills);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTools()
    {
        // Arrange
        var tools = ImmutableList<string>.Empty.AddRange(s_tools3);

        // Act
        var dto = new AgentCapabilitiesDto
        {
            Tools = tools
        };

        // Assert
        Assert.Equal(3, dto.Tools.Count);
        Assert.Contains("FileReadTool", dto.Tools);
        Assert.Contains("WebScrapeTool", dto.Tools);
        Assert.Contains(ToolSearch, dto.Tools);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingLanguages()
    {
        // Arrange
        var languages = ImmutableList<string>.Empty.AddRange(s_languages3);

        // Act
        var dto = new AgentCapabilitiesDto
        {
            Languages = languages
        };

        // Assert
        Assert.Equal(3, dto.Languages.Count);
        Assert.Contains("English", dto.Languages);
        Assert.Contains("French", dto.Languages);
        Assert.Contains("Spanish", dto.Languages);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingOverallConfidence()
    {
        // Arrange & Act
        var dto = new AgentCapabilitiesDto
        {
            OverallConfidence = "High"
        };

        // Assert
        Assert.Equal("High", dto.OverallConfidence);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingSpecializations()
    {
        // Arrange
        var specializations = ImmutableList<string>.Empty.AddRange(s_specializations3);

        // Act
        var dto = new AgentCapabilitiesDto
        {
            Specializations = specializations
        };

        // Assert
        Assert.Equal(3, dto.Specializations.Count);
        Assert.Contains("Machine Learning", dto.Specializations);
        Assert.Contains("Web Development", dto.Specializations);
        Assert.Contains("DevOps", dto.Specializations);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCertificationLevel()
    {
        // Arrange & Act
        var dto = new AgentCapabilitiesDto
        {
            CertificationLevel = "Expert"
        };

        // Assert
        Assert.Equal("Expert", dto.CertificationLevel);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedProperty_WhenUsingWithMethod()
    {
        // Arrange
        var originalDto = new AgentCapabilitiesDto
        {
            OverallConfidence = "Medium"
        };

        // Act
        var updatedDto = originalDto with { OverallConfidence = "High" };

        // Assert
        Assert.Equal("Medium", originalDto.OverallConfidence);
        Assert.Equal("High", updatedDto.OverallConfidence);
        Assert.NotSame(originalDto, updatedDto);
    }

    [Fact]
    public void ShouldBeEqual_WhenUsingEqualityWithSameValues()
    {
        // Arrange
        var skills = ImmutableList<string>.Empty.Add("C#");
        var dto1 = new AgentCapabilitiesDto
        {
            Skills = skills,
            OverallConfidence = "High"
        };
        var dto2 = new AgentCapabilitiesDto
        {
            Skills = skills,
            OverallConfidence = "High"
        };

        // Act & Assert
        Assert.Equal(dto1, dto2);
        Assert.True(dto1.Equals(dto2));
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEqualityWithDifferentValues()
    {
        // Arrange
        var dto1 = new AgentCapabilitiesDto
        {
            OverallConfidence = "High"
        };
        var dto2 = new AgentCapabilitiesDto
        {
            OverallConfidence = "Low"
        };

        // Act & Assert
        Assert.NotEqual(dto1, dto2);
        Assert.False(dto1.Equals(dto2));
    }

    [Fact]
    public void ShouldBeValid_WhenUsingSkillsWithEmptyCollection()
    {
        // Act
        var dto = new AgentCapabilitiesDto
        {
            Skills = []
        };

        // Assert
        Assert.True(dto.Skills.IsEmpty);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingToolsWithEmptyCollection()
    {
        // Act
        var dto = new AgentCapabilitiesDto
        {
            Tools = []
        };

        // Assert
        Assert.True(dto.Tools.IsEmpty);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingLanguagesWithEmptyCollection()
    {
        // Act
        var dto = new AgentCapabilitiesDto
        {
            Languages = []
        };

        // Assert
        Assert.True(dto.Languages.IsEmpty);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingSpecializationsWithEmptyCollection()
    {
        // Act
        var dto = new AgentCapabilitiesDto
        {
            Specializations = []
        };

        // Assert
        Assert.True(dto.Specializations.IsEmpty);
    }

    [Fact]
    public void ShouldBeValid_WhenAccessingAllPropertiesWithCompleteData()
    {
        // Arrange
        var skills = ImmutableList<string>.Empty.AddRange(s_skills2);
        var tools = ImmutableList<string>.Empty.AddRange(s_tools2);
        var languages = ImmutableList<string>.Empty.AddRange(s_languages2);
        var specializations = ImmutableList<string>.Empty.AddRange(s_specializations2);

        // Act
        var dto = new AgentCapabilitiesDto
        {
            Skills = skills,
            Tools = tools,
            Languages = languages,
            OverallConfidence = "Expert",
            Specializations = specializations,
            CertificationLevel = "Advanced"
        };

        // Assert
        Assert.Equal(2, dto.Skills.Count);
        Assert.Equal(2, dto.Tools.Count);
        Assert.Equal(2, dto.Languages.Count);
        Assert.Equal("Expert", dto.OverallConfidence);
        Assert.Equal(2, dto.Specializations.Count);
        Assert.Equal("Advanced", dto.CertificationLevel);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Expert")]
    public void ShouldBeValid_WhenUsingOverallConfidenceWithValidValues(string confidence)
    {
        // Act
        var dto = new AgentCapabilitiesDto
        {
            OverallConfidence = confidence
        };

        // Assert
        Assert.Equal(confidence, dto.OverallConfidence);
    }

    [Theory]
    [InlineData("Beginner")]
    [InlineData("Standard")]
    [InlineData("Advanced")]
    [InlineData("Expert")]
    [InlineData("Master")]
    public void ShouldBeValid_WhenUsingCertificationLevelWithValidValues(string level)
    {
        // Act
        var dto = new AgentCapabilitiesDto
        {
            CertificationLevel = level
        };

        // Assert
        Assert.Equal(level, dto.CertificationLevel);
    }

    [Fact]
    public void ShouldAllowDuplicates_WhenUsingSkillsWithDuplicateValues()
    {
        // Arrange
        var skills = ImmutableList<string>.Empty.AddRange(s_skillsDuplicate);

        // Act
        var dto = new AgentCapabilitiesDto
        {
            Skills = skills
        };

        // Assert
        Assert.Equal(3, dto.Skills.Count);
        Assert.Equal(2, dto.Skills.Count(s => s == "C#"));
    }

    [Fact]
    public void ShouldNotBeModifiableDirectly_WhenUsingImmutableCollections()
    {
        // Arrange
        var dto = new AgentCapabilitiesDto
        {
            Skills = ImmutableList<string>.Empty.Add("C#")
        };

        // Act & Assert - Should compile and work as expected
        var newSkills = dto.Skills.Add("Python"); // This creates a new collection
        Assert.Single(dto.Skills); // Original unchanged
        Assert.Equal(2, newSkills.Count); // New collection has both
    }
}
