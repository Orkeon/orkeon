using Orkeon.Domain.Agent.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Common;

public class AgentCapabilityTests
{
    private static readonly string[] CsvJsonXmlFormats = ["csv", "json", "xml"];
    private static readonly string[] RegressionClassificationClustering = ["regression", "classification", "clustering"];
    private static readonly string[] DeepLearningNnNlp = ["deep_learning", "neural_networks", "nlp"];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateAgentCapability_WhenConstructingWithAllParameters()
    {
        // Arrange
        var name = "DataProcessing";
        var description = "Ability to process and analyze data";
        var requiredSkills = new List<string> { "data_analysis", "statistics" };
        var requiredTools = new List<string> { "pandas", "numpy" };
        var confidenceLevel = 0.85;
        var parameters = new Dictionary<string, object>
        {
            ["maxDataSize"] = 1000000,
            ["supportedFormats"] = CsvJsonXmlFormats
        };

        // Act
        var capability = AgentCapability.Create(
            name,
            description,
            confidenceLevel,
            requiredSkills,
            requiredTools,
            parameters);

        // Assert
        Assert.Equal(name, capability.Name);
        Assert.Equal(description, capability.Description);
        Assert.Equal(requiredSkills, capability.RequiredSkills);
        Assert.Equal(requiredTools, capability.RequiredTools);
        Assert.Equal(confidenceLevel, capability.ConfidenceLevel);
        Assert.Equal(parameters, capability.Parameters);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var name = "BasicCapability";
        var description = "A basic capability";

        // Act
        var capability = AgentCapability.Create(name, description);

        // Assert
        Assert.Equal(name, capability.Name);
        Assert.Equal(description, capability.Description);
        Assert.Empty(capability.RequiredSkills);
        Assert.Empty(capability.RequiredTools);
        Assert.Equal(1.0, capability.ConfidenceLevel);
        Assert.Empty(capability.Parameters);
    }

    [Fact]
    public void ShouldUseEmptyCollections_WhenConstructingWithNullOptionalParameters()
    {
        // Arrange
        var name = "NullableCapability";
        var description = "Testing null parameters";

        // Act
        var capability = AgentCapability.Create(
            name,
            description,
            requiredSkills: null,
            requiredTools: null,
            confidenceLevel: 0.7,
            parameters: null);

        // Assert
        Assert.NotNull(capability.RequiredSkills);
        Assert.Empty(capability.RequiredSkills);
        Assert.NotNull(capability.RequiredTools);
        Assert.Empty(capability.RequiredTools);
        Assert.NotNull(capability.Parameters);
        Assert.Empty(capability.Parameters);
        Assert.Equal(0.7, capability.ConfidenceLevel);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            AgentCapability.Create(null!, "description"));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullDescription()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            AgentCapability.Create("name", null!));
        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldClampToOne_WhenConstructingWithConfidenceLevelAboveOne()
    {
        // Arrange & Act
        var capability = AgentCapability.Create(
            "HighConfidence",
            "Testing confidence clamping",
            confidenceLevel: 1.5);

        // Assert
        Assert.Equal(1.0, capability.ConfidenceLevel);
    }

    [Fact]
    public void ShouldClampToZero_WhenConstructingWithConfidenceLevelBelowZero()
    {
        // Arrange & Act
        var capability = AgentCapability.Create(
            "LowConfidence",
            "Testing confidence clamping",
            confidenceLevel: -0.5);

        // Assert
        Assert.Equal(0, capability.ConfidenceLevel);
    }

    [Fact]
    public void ShouldClampCorrectly_WhenConstructingWithVariousConfidenceLevels()
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
            (input: double.MinValue, expected: 0.0)
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var capability = AgentCapability.Create("Test", "Test", confidenceLevel: input);
            Assert.Equal(expected, capability.ConfidenceLevel);
        }
    }

    #endregion

    #region CanExecute Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingCanExecuteWithAllRequirementsMet()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "WebScraping",
            "Ability to scrape web content",
            requiredSkills: ["html_parsing", "http_requests"],
            requiredTools: ["beautifulsoup", "requests"]);

        var availableSkills = new List<string> { "html_parsing", "http_requests", "data_processing" };
        var availableTools = new List<string> { "beautifulsoup", "requests", "pandas" };

        // Act
        var result = capability.CanExecute(availableSkills, availableTools);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingCanExecuteWithMissingSkill()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "DataAnalysis",
            "Analyze complex datasets",
            requiredSkills: ["statistics", "machine_learning"],
            requiredTools: ["scikit-learn"]);

        var availableSkills = new List<string> { "statistics" }; // Missing machine_learning
        var availableTools = new List<string> { "scikit-learn" };

        // Act
        var result = capability.CanExecute(availableSkills, availableTools);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingCanExecuteWithMissingTool()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "ImageProcessing",
            "Process and manipulate images",
            requiredSkills: ["image_manipulation"],
            requiredTools: ["opencv", "pillow"]);

        var availableSkills = new List<string> { "image_manipulation" };
        var availableTools = new List<string> { "opencv" }; // Missing pillow

        // Act
        var result = capability.CanExecute(availableSkills, availableTools);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingCanExecuteWithNoRequirements()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "BasicTask",
            "A simple task with no requirements");

        var availableSkills = new List<string>();
        var availableTools = new List<string>();

        // Act
        var result = capability.CanExecute(availableSkills, availableTools);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingCanExecuteWithEmptyAvailableResources()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "ComplexTask",
            "Requires many resources",
            requiredSkills: ["skill1"],
            requiredTools: ["tool1"]);

        var availableSkills = new List<string>();
        var availableTools = new List<string>();

        // Act
        var result = capability.CanExecute(availableSkills, availableTools);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingCanExecuteWithExactMatchingResources()
    {
        // Arrange
        var requiredSkills = new List<string> { "skill1", "skill2", "skill3" };
        var requiredTools = new List<string> { "tool1", "tool2" };

        var capability = AgentCapability.Create(
            "ExactMatch",
            "Testing exact matches",
            requiredSkills: requiredSkills,
            requiredTools: requiredTools);

        // Act
        var result = capability.CanExecute(requiredSkills, requiredTools);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldBeCaseSensitive_WhenUsingCanExecuteWithCaseSensitiveSkills()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "CaseSensitive",
            "Testing case sensitivity",
            requiredSkills: ["JavaScript", "Python"]);

        var availableSkills = new List<string> { "javascript", "python" }; // Different case
        var availableTools = new List<string>();

        // Act
        var result = capability.CanExecute(availableSkills, availableTools);

        // Assert
        Assert.False(result); // Should be case sensitive
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var skills = new List<string> { "skill1", "skill2" };
        var tools = new List<string> { "tool1" };
        var parameters = new Dictionary<string, object> { ["key"] = "value" };

        var capability1 = AgentCapability.Create(
            "Capability",
            "Description",
            0.8,
            skills,
            tools,
            parameters);

        var capability2 = AgentCapability.Create(
            "Capability",
            "Description",
            0.8,
            skills,
            tools,
            parameters);

        // Act & Assert
        Assert.Equal(capability1, capability2);
        Assert.True(capability1 == capability2);
        Assert.False(capability1 != capability2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentName()
    {
        // Arrange
        var capability1 = AgentCapability.Create("Capability1", "Description");
        var capability2 = AgentCapability.Create("Capability2", "Description");

        // Act & Assert
        Assert.NotEqual(capability1, capability2);
        Assert.False(capability1 == capability2);
        Assert.True(capability1 != capability2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentDescription()
    {
        // Arrange
        var capability1 = AgentCapability.Create("Name", "Description1");
        var capability2 = AgentCapability.Create("Name", "Description2");

        // Act & Assert
        Assert.NotEqual(capability1, capability2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentSkills()
    {
        // Arrange
        var capability1 = AgentCapability.Create(
            "Name",
            "Description",
            requiredSkills: ["skill1"]);

        var capability2 = AgentCapability.Create(
            "Name",
            "Description",
            requiredSkills: ["skill2"]);

        // Act & Assert
        Assert.NotEqual(capability1, capability2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentConfidenceLevel()
    {
        // Arrange
        var capability1 = AgentCapability.Create("Name", "Description", confidenceLevel: 0.7);
        var capability2 = AgentCapability.Create("Name", "Description", confidenceLevel: 0.8);

        // Act & Assert
        Assert.NotEqual(capability1, capability2);
    }

    #endregion

    #region Record Features Tests

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = AgentCapability.Create(
            "Original",
            "Original description",
            requiredSkills: ["skill1"],
            confidenceLevel: 0.7);

        // Act - Since AgentCapability is immutable, we create a new instance
        var modified = AgentCapability.Create(
            original.Name,
            original.Description,
            requiredSkills: ["skill2", "skill3"],
            requiredTools: original.RequiredTools,
            confidenceLevel: 0.9,
            parameters: original.Parameters);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(original.Name, modified.Name);
        Assert.Equal(original.Description, modified.Description);
        Assert.NotEqual(original.ConfidenceLevel, modified.ConfidenceLevel);
        Assert.NotEqual(original.RequiredSkills, modified.RequiredSkills);
        Assert.Equal(0.9, modified.ConfidenceLevel);
        Assert.Equal(2, modified.RequiredSkills.Count);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var capability = AgentCapability.Create(
            "TestCapability",
            "Test description",
            requiredSkills: ["skill1"],
            requiredTools: ["tool1"],
            confidenceLevel: 0.9);

        // Act
        var result = capability.ToString();

        // Assert
        Assert.Contains("AgentCapability", result);
        Assert.Contains("Name = TestCapability", result);
        Assert.Contains("Description = Test description", result);
    }

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var skills = new List<string> { "skill1" };
        var tools = new List<string> { "tool1" };

        var capability1 = AgentCapability.Create(
            "Name",
            "Description",
            0.5,
            skills,
            tools);

        var capability2 = AgentCapability.Create(
            "Name",
            "Description",
            0.5,
            skills,
            tools);

        // Act
        var hash1 = capability1.GetHashCode();
        var hash2 = capability2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }


    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldMultipleCapabilitiesForAgent_WhenUsingComplexScenario()
    {
        // Arrange
        var capabilities = new List<AgentCapability>
        {
            AgentCapability.Create(
                "WebScraping",
                "Extract data from websites",
                requiredSkills: ["html_parsing", "css_selectors"],
                requiredTools: ["selenium", "beautifulsoup"],
                confidenceLevel: 0.9),

            AgentCapability.Create(
                "DataAnalysis",
                "Analyze structured data",
                requiredSkills: ["statistics", "data_visualization"],
                requiredTools: ["pandas", "matplotlib"],
                confidenceLevel: 0.85),

            AgentCapability.Create(
                "ReportGeneration",
                "Generate comprehensive reports",
                requiredSkills: ["writing", "data_visualization"],
                requiredTools: ["markdown", "charts"],
                confidenceLevel: 0.95)
        };

        var agentSkills = new List<string>
        {
            "html_parsing",
            "css_selectors",
            "statistics",
            "writing"
        };

        var agentTools = new List<string>
        {
            "selenium",
            "beautifulsoup",
            "pandas",
            "markdown"
        };

        // Act
        var executableCapabilities = capabilities
            .Where(c => c.CanExecute(agentSkills, agentTools))
            .OrderByDescending(c => c.ConfidenceLevel)
            .ToList();

        // Assert
        Assert.Single(executableCapabilities);
        Assert.Equal("WebScraping", executableCapabilities[0].Name);
    }

    [Fact]
    public void ShouldCapabilityWithComplexParameters_WhenUsingComplexScenario()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["maxRetries"] = 3,
            ["timeout"] = TimeoutQuick,
            ["supportedFormats"] = new List<string> { "json", "xml", "csv" },
            ["configuration"] = new Dictionary<string, object>
            {
                ["useCache"] = true,
                ["cacheSize"] = 100,
                ["compressionLevel"] = 5
            },
            ["validators"] = new List<Func<string, bool>>
            {
                s => !string.IsNullOrEmpty(s),
                s => s.Length < 1000
            }
        };

        var capability = AgentCapability.Create(
            "DataProcessing",
            "Complex data processing capability",
            requiredSkills: ["data_validation", "data_transformation"],
            requiredTools: ["validator", "transformer"],
            confidenceLevel: 0.88,
            parameters: parameters);

        // Act & Assert
        Assert.Equal(3, capability.Parameters["maxRetries"]);
        Assert.Equal(TimeoutQuick, capability.Parameters["timeout"]);

        var formats = capability.Parameters["supportedFormats"] as List<string>;
        Assert.NotNull(formats);
        Assert.Contains("json", formats);
        Assert.Contains("xml", formats);
        Assert.Contains("csv", formats);

        var config = capability.Parameters["configuration"] as Dictionary<string, object>;
        Assert.NotNull(config);
        Assert.True((bool)config["useCache"]);
        Assert.Equal(100, config["cacheSize"]);
    }

    [Fact]
    public void ShouldCapabilityEvolution_WhenUsingComplexScenario()
    {
        // Arrange - Initial capability
        var initialCapability = AgentCapability.Create(
            "BasicAnalysis",
            "Simple data analysis",
            requiredSkills: ["basic_stats"],
            requiredTools: ["calculator"],
            confidenceLevel: 0.6,
            parameters: new Dictionary<string, object> { ["version"] = "1.0" });

        // Act - Evolve capability over time
        var enhancedCapability = AgentCapability.Create(
            initialCapability.Name,
            initialCapability.Description,
            requiredSkills: ["basic_stats", "advanced_stats", "machine_learning"],
            requiredTools: ["calculator", "numpy", "scikit-learn"],
            confidenceLevel: 0.85,
            parameters: new Dictionary<string, object>
            {
                ["version"] = "2.0",
                ["features"] = RegressionClassificationClustering
            });

        var expertCapability = AgentCapability.Create(
            "ExpertAnalysis",
            "Advanced AI-powered analysis",
            requiredSkills: enhancedCapability.RequiredSkills,
            requiredTools: enhancedCapability.RequiredTools,
            confidenceLevel: 0.95,
            parameters: new Dictionary<string, object>
            {
                ["version"] = "3.0",
                ["features"] = DeepLearningNnNlp,
                ["modelAccuracy"] = 0.98
            });

        // Assert
        Assert.Equal("BasicAnalysis", initialCapability.Name);
        Assert.Equal("BasicAnalysis", enhancedCapability.Name);
        Assert.Equal("ExpertAnalysis", expertCapability.Name);

        Assert.Equal(0.6, initialCapability.ConfidenceLevel);
        Assert.Equal(0.85, enhancedCapability.ConfidenceLevel);
        Assert.Equal(0.95, expertCapability.ConfidenceLevel);

        Assert.Single(initialCapability.RequiredSkills);
        Assert.Equal(3, enhancedCapability.RequiredSkills.Count);
        Assert.Equal(3, expertCapability.RequiredSkills.Count);
    }

    [Fact]
    public void ShouldCapabilityMatching_WhenUsingComplexScenario()
    {
        // Arrange
        var taskRequirements = new
        {
            RequiredCapabilities = new List<string> { "DataExtraction", "DataTransformation", "Reporting" },
            MinConfidence = 0.8
        };

        var agentCapabilities = new List<AgentCapability>
        {
            AgentCapability.Create(
                "DataExtraction",
                "Extract data from various sources",
                requiredSkills: ["parsing", "api_calls"],
                confidenceLevel: 0.85),

            AgentCapability.Create(
                "DataTransformation",
                "Transform and clean data",
                requiredSkills: ["data_cleaning", "etl"],
                confidenceLevel: 0.9),

            AgentCapability.Create(
                "BasicReporting",
                "Generate simple reports",
                requiredSkills: ["writing"],
                confidenceLevel: 0.7), // Below threshold
            
            AgentCapability.Create(
                "Reporting",
                "Generate comprehensive reports",
                requiredSkills: ["writing", "visualization"],
                confidenceLevel: 0.82)
        };

        var availableSkills = new List<string>
        {
            "parsing", "api_calls", "data_cleaning", "etl", "writing", "visualization"
        };
        var availableTools = new List<string>();

        // Act
        var matchingCapabilities = agentCapabilities
            .Where(c => taskRequirements.RequiredCapabilities.Contains(c.Name))
            .Where(c => c.ConfidenceLevel >= taskRequirements.MinConfidence)
            .Where(c => c.CanExecute(availableSkills, availableTools))
            .ToList();

        // Assert
        Assert.Equal(3, matchingCapabilities.Count);
        Assert.All(matchingCapabilities, c => Assert.True(c.ConfidenceLevel >= 0.8));
        Assert.Contains(matchingCapabilities, c => c.Name == "DataExtraction");
        Assert.Contains(matchingCapabilities, c => c.Name == "DataTransformation");
        Assert.Contains(matchingCapabilities, c => c.Name == "Reporting");
        Assert.DoesNotContain(matchingCapabilities, c => c.Name == "BasicReporting");
    }

    #endregion
}
