using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Application.Tests.DTOs.Agents;

public class CreateAgentRequestTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var request = new CreateAgentRequest { Role = "Test", Goal = "Test goal" };

        // Assert
        Assert.Equal("Test", request.Role);
        Assert.Equal("Test goal", request.Goal);
        Assert.Equal(string.Empty, request.Backstory);
        Assert.NotNull(request.Tools);
        Assert.Empty(request.Tools);
        Assert.Null(request.Settings);
        Assert.Null(request.LlmConfig);
        Assert.NotNull(request.Capabilities);
        Assert.Empty(request.Capabilities);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingRoleWithValidValue()
    {
        // Arrange & Act
        var request = new CreateAgentRequest
        {
            Role = RoleSeniorDeveloper,
            Goal = "Test goal"
        };

        // Assert
        Assert.Equal(RoleSeniorDeveloper, request.Role);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingGoalWithValidValue()
    {
        // Arrange & Act
        var request = new CreateAgentRequest
        {
            Role = "Test",
            Goal = "Develop high-quality software applications using best practices"
        };

        // Assert
        Assert.Equal("Develop high-quality software applications using best practices", request.Goal);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingBackstoryWithValidValue()
    {
        // Arrange & Act
        var request = new CreateAgentRequest
        {
            Role = "Test",
            Goal = "Test goal",
            Backstory = "Experienced software developer with 10+ years in full-stack development"
        };

        // Assert
        Assert.Equal("Experienced software developer with 10+ years in full-stack development", request.Backstory);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingTools()
    {
        // Arrange
        var request = new CreateAgentRequest { Role = "Test", Goal = "Test goal" };

        // Act
        var tools = new List<string> { "FileReadTool", "WebScrapeTool", ToolSearch };
        request = request with { Tools = tools };

        // Assert
        Assert.Equal(3, request.Tools.Count);
        Assert.Contains("FileReadTool", request.Tools);
        Assert.Contains("WebScrapeTool", request.Tools);
        Assert.Contains(ToolSearch, request.Tools);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingCapabilities()
    {
        // Arrange
        var request = new CreateAgentRequest { Role = "Test", Goal = "Test goal" };

        // Act
        var capabilities = new List<string> { "Analysis", "Research", "Documentation" };
        request = request with { Capabilities = capabilities };

        // Assert
        Assert.Equal(3, request.Capabilities.Count);
        Assert.Contains("Analysis", request.Capabilities);
        Assert.Contains("Research", request.Capabilities);
        Assert.Contains("Documentation", request.Capabilities);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingSettingsWithValidAgentSettings()
    {
        // Arrange
        var settings = new AgentSettingsDto
        {
            Verbose = true,
            AllowDelegation = false,
            MaxIterations = 10
        };

        // Act
        var request = new CreateAgentRequest
        {
            Role = "Test",
            Goal = "Test goal",
            Settings = settings
        };

        // Assert
        Assert.Equal(settings, request.Settings);
        Assert.True(request.Settings.Verbose);
        Assert.False(request.Settings.AllowDelegation);
        Assert.Equal(10, request.Settings.MaxIterations);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingLlmConfigWithValidConfiguration()
    {
        // Arrange
        var llmConfig = new LlmConfigDto { Provider = "openai", Model = "gpt-4" };

        // Act
        var request = new CreateAgentRequest
        {
            Role = "Test",
            Goal = "Test goal",
            LlmConfig = llmConfig
        };

        // Assert
        Assert.Equal(llmConfig, request.LlmConfig);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteRequestWithAllProperties()
    {
        // Arrange
        var settings = new AgentSettingsDto
        {
            Verbose = true,
            MaxIterations = 5
        };
        var tools = new List<string> { "FileReadTool", ToolSearch };
        var capabilities = new List<string> { "Research", "Analysis" };

        // Act
        var request = new CreateAgentRequest
        {
            Role = "Research Analyst",
            Goal = "Conduct thorough research and provide detailed analysis",
            Backstory = "Expert researcher with background in data analysis and reporting",
            Tools = tools,
            Settings = settings,
            Capabilities = capabilities
        };

        // Assert
        Assert.Equal("Research Analyst", request.Role);
        Assert.Equal("Conduct thorough research and provide detailed analysis", request.Goal);
        Assert.Equal("Expert researcher with background in data analysis and reporting", request.Backstory);
        Assert.Equal(2, request.Tools.Count);
        Assert.Equal(2, request.Capabilities.Count);
        Assert.NotNull(request.Settings);
    }
}

public class UpdateAgentRequestTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var request = new UpdateAgentRequest();

        // Assert
        Assert.Null(request.Goal);
        Assert.Null(request.Backstory);
        Assert.Null(request.Tools);
        Assert.Null(request.Settings);
        Assert.Null(request.LlmConfig);
        Assert.Null(request.Capabilities);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingGoalWithValidValue()
    {
        // Arrange & Act
        var request = new UpdateAgentRequest
        {
            Goal = "Updated goal that meets minimum requirements"
        };

        // Assert
        Assert.Equal("Updated goal that meets minimum requirements", request.Goal);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingBackstoryWithValidValue()
    {
        // Arrange & Act
        var request = new UpdateAgentRequest
        {
            Backstory = "Updated backstory that meets minimum requirements for testing"
        };

        // Assert
        Assert.Equal("Updated backstory that meets minimum requirements for testing", request.Backstory);
    }

    [Fact]
    public void ShouldBeValid_WhenAccessingAllPropertiesWithValidValues()
    {
        // Arrange
        var tools = new List<string> { "UpdatedTool1", "UpdatedTool2" };
        var capabilities = new List<string> { "UpdatedCapability1", "UpdatedCapability2" };
        var settings = new AgentSettingsDto { Verbose = true };

        // Act
        var request = new UpdateAgentRequest
        {
            Goal = "Updated goal that meets minimum requirements",
            Backstory = "Updated backstory that meets minimum requirements for testing",
            Tools = tools,
            Settings = settings,
            Capabilities = capabilities
        };

        // Assert
        Assert.Equal("Updated goal that meets minimum requirements", request.Goal);
        Assert.Equal("Updated backstory that meets minimum requirements for testing", request.Backstory);
        Assert.Equal(2, request.Tools.Count);
        Assert.Equal(2, request.Capabilities.Count);
        Assert.NotNull(request.Settings);
        Assert.True(request.Settings.Verbose);
    }
}

public class AgentSettingsDtoTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var settings = new AgentSettingsDto();

        // Assert
        Assert.False(settings.Verbose);
        Assert.True(settings.AllowDelegation);
        Assert.Null(settings.MaxIterations);
        Assert.Null(settings.MaxRPM);
        Assert.Null(settings.MaxExecutionTimeSeconds);
        Assert.Null(settings.Memory);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingVerbose()
    {
        // Act
        var settings = new AgentSettingsDto
        {
            Verbose = true
        };

        // Assert
        Assert.True(settings.Verbose);
    }

    [Fact]
    public void ShouldBeSettable_WhenAllowingDelegation()
    {
        // Act
        var settings = new AgentSettingsDto
        {
            AllowDelegation = false
        };

        // Assert
        Assert.False(settings.AllowDelegation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void ShouldBeValid_WhenUsingMaxIterationsWithValidValues(int maxIterations)
    {
        // Act
        var settings = new AgentSettingsDto
        {
            MaxIterations = maxIterations
        };

        // Assert
        Assert.Equal(maxIterations, settings.MaxIterations);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(10.5)]
    [InlineData(1000.0)]
    public void ShouldBeValid_WhenUsingMaxRPMWithValidValues(double maxRpm)
    {
        // Act
        var settings = new AgentSettingsDto
        {
            MaxRPM = maxRpm
        };

        // Assert
        Assert.Equal(maxRpm, settings.MaxRPM);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1800)]
    [InlineData(3600)]
    public void ShouldBeValid_WhenUsingMaxExecutionTimeSecondsWithValidValues(int maxTime)
    {
        // Act
        var settings = new AgentSettingsDto
        {
            MaxExecutionTimeSeconds = maxTime
        };

        // Assert
        Assert.Equal(maxTime, settings.MaxExecutionTimeSeconds);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingMemoryWithValidSettings()
    {
        // Arrange
        var memorySettings = new AgentMemorySettingsDto
        {
            Enabled = true,
            MaxShortTermItems = 50
        };

        // Act
        var settings = new AgentSettingsDto
        {
            Memory = memorySettings
        };

        // Assert
        Assert.Equal(memorySettings, settings.Memory);
        Assert.True(settings.Memory.Enabled);
        Assert.Equal(50, settings.Memory.MaxShortTermItems);
    }

    [Fact]
    public void ShouldBeValid_WhenAccessingAllPropertiesWithValidValues()
    {
        // Arrange
        var memorySettings = new AgentMemorySettingsDto
        {
            Enabled = false,
            MaxShortTermItems = 200
        };

        // Act
        var settings = new AgentSettingsDto
        {
            Verbose = true,
            AllowDelegation = false,
            MaxIterations = 25,
            MaxRPM = 50.0,
            MaxExecutionTimeSeconds = 1200,
            Memory = memorySettings
        };

        // Assert
        Assert.True(settings.Verbose);
        Assert.False(settings.AllowDelegation);
        Assert.Equal(25, settings.MaxIterations);
        Assert.Equal(50.0, settings.MaxRPM);
        Assert.Equal(1200, settings.MaxExecutionTimeSeconds);
        Assert.NotNull(settings.Memory);
        Assert.False(settings.Memory.Enabled);
    }
}

public class AgentMemorySettingsDtoTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var settings = new AgentMemorySettingsDto();

        // Assert
        Assert.True(settings.Enabled);
        Assert.Equal(100, settings.MaxShortTermItems);
        Assert.True(settings.PersistLongTerm);
        Assert.Equal(0.7, settings.RelevanceThreshold);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEnabled()
    {
        // Act
        var settings = new AgentMemorySettingsDto
        {
            Enabled = false
        };

        // Assert
        Assert.False(settings.Enabled);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(1000)]
    public void ShouldBeValid_WhenUsingMaxShortTermItemsWithValidValues(int maxItems)
    {
        // Act
        var settings = new AgentMemorySettingsDto
        {
            MaxShortTermItems = maxItems
        };

        // Assert
        Assert.Equal(maxItems, settings.MaxShortTermItems);
    }

    [Fact]
    public void ShouldBeSettable_WhenPersistingLongTerm()
    {
        // Act
        var settings = new AgentMemorySettingsDto
        {
            PersistLongTerm = false
        };

        // Assert
        Assert.False(settings.PersistLongTerm);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ShouldBeValid_WhenUsingRelevanceThresholdWithValidValues(double threshold)
    {
        // Act
        var settings = new AgentMemorySettingsDto
        {
            RelevanceThreshold = threshold
        };

        // Assert
        Assert.Equal(threshold, settings.RelevanceThreshold);
    }

    [Fact]
    public void ShouldBeValid_WhenAccessingAllPropertiesWithValidValues()
    {
        // Act
        var settings = new AgentMemorySettingsDto
        {
            Enabled = false,
            MaxShortTermItems = 250,
            PersistLongTerm = false,
            RelevanceThreshold = 0.8
        };

        // Assert
        Assert.False(settings.Enabled);
        Assert.Equal(250, settings.MaxShortTermItems);
        Assert.False(settings.PersistLongTerm);
        Assert.Equal(0.8, settings.RelevanceThreshold);
    }
}
