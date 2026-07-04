using Orkeon.Domain.Crew;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// Tests for CrewConfigurationUpdate options class.
/// Verifies that the partial-update DTO exposes nullable properties correctly.
/// </summary>
public class CrewConfigurationUpdateTests
{
    [Fact]
    public void ShouldDefaultAllPropertiesToNull_WhenCreatingWithDefaults()
    {
        // Act
        var update = new CrewConfigurationUpdate();

        // Assert
        Assert.Null(update.Verbose);
        Assert.Null(update.Planning);
        Assert.Null(update.MaxRpm);
        Assert.Null(update.ShareCrew);
        Assert.Null(update.OutputLogFile);
        Assert.Null(update.Language);
        Assert.Null(update.FullOutput);
        Assert.Null(update.MemoryEnabled);
    }

    [Fact]
    public void ShouldSetVerbose_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { Verbose = true };

        // Assert
        Assert.True(update.Verbose);
    }

    [Fact]
    public void ShouldSetPlanning_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { Planning = true };

        // Assert
        Assert.True(update.Planning);
    }

    [Fact]
    public void ShouldSetMaxRpm_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { MaxRpm = 200 };

        // Assert
        Assert.Equal(200, update.MaxRpm);
    }

    [Fact]
    public void ShouldSetShareCrew_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { ShareCrew = false };

        // Assert
        Assert.False(update.ShareCrew);
    }

    [Fact]
    public void ShouldSetOutputLogFile_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { OutputLogFile = "/var/log/crew.log" };

        // Assert
        Assert.Equal("/var/log/crew.log", update.OutputLogFile);
    }

    [Fact]
    public void ShouldSetLanguage_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { Language = "fr" };

        // Assert
        Assert.Equal("fr", update.Language);
    }

    [Fact]
    public void ShouldSetFullOutput_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { FullOutput = true };

        // Assert
        Assert.True(update.FullOutput);
    }

    [Fact]
    public void ShouldSetMemoryEnabled_WhenInitializingWithValue()
    {
        // Act
        var update = new CrewConfigurationUpdate { MemoryEnabled = true };

        // Assert
        Assert.True(update.MemoryEnabled);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenInitializingWithAllValues()
    {
        // Act
        var update = new CrewConfigurationUpdate
        {
            Verbose = true,
            Planning = true,
            MaxRpm = 50,
            ShareCrew = false,
            OutputLogFile = "/tmp/output.log",
            Language = "de",
            FullOutput = true,
            MemoryEnabled = true
        };

        // Assert
        Assert.True(update.Verbose);
        Assert.True(update.Planning);
        Assert.Equal(50, update.MaxRpm);
        Assert.False(update.ShareCrew);
        Assert.Equal("/tmp/output.log", update.OutputLogFile);
        Assert.Equal("de", update.Language);
        Assert.True(update.FullOutput);
        Assert.True(update.MemoryEnabled);
    }

    [Fact]
    public void ShouldAllowFalseValues_WhenInitializingBoolProperties()
    {
        // Act
        var update = new CrewConfigurationUpdate
        {
            Verbose = false,
            Planning = false,
            ShareCrew = false,
            FullOutput = false,
            MemoryEnabled = false
        };

        // Assert
        Assert.False(update.Verbose);
        Assert.False(update.Planning);
        Assert.False(update.ShareCrew);
        Assert.False(update.FullOutput);
        Assert.False(update.MemoryEnabled);
    }

    [Fact]
    public void ShouldPreserveNullForUnsetProperties_WhenOnlySomeAreSet()
    {
        // Act
        var update = new CrewConfigurationUpdate
        {
            Verbose = true,
            Language = "es"
        };

        // Assert
        Assert.True(update.Verbose);
        Assert.Equal("es", update.Language);
        Assert.Null(update.Planning);
        Assert.Null(update.MaxRpm);
        Assert.Null(update.ShareCrew);
        Assert.Null(update.OutputLogFile);
        Assert.Null(update.FullOutput);
        Assert.Null(update.MemoryEnabled);
    }
}
