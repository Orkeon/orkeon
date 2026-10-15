using System.Collections.Immutable;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Task.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.DTOs.Crews;

public class CrewDtoTests
{
    private static readonly string[] s_criticalAutomated = ["critical", "automated"];
    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = "crew-123",
            Name = "Test Crew",
            Description = "A test crew for development",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        // Assert
        Assert.Equal("crew-123", dto.Id);
        Assert.Equal("Test Crew", dto.Name);
        Assert.Equal("A test crew for development", dto.Description);
        Assert.Equal("Sequential", dto.ProcessType);
        Assert.Equal("Ready", dto.Status);
        Assert.Equal("Normal", dto.Verbosity);
    }

    [Fact]
    public void ShouldBeSetCorrectly_WhenUsingDefaultValues()
    {
        // Arrange & Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Idle",
            Verbosity = "Normal"
        };

        // Assert
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldBeSetAutomatically_WhenUsingCreatedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dto = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew",
            Description = "Description",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(beforeCreation <= dto.CreatedAt);
        Assert.True(dto.CreatedAt <= afterCreation);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "crew-123";
        var name = "Test Crew";
        var description = "A test crew";
        var processType = "Sequential";
        var status = "Ready";
        var verbosity = "Normal";
        var createdAt = DateTime.UtcNow; // Use same timestamp for both DTOs

        // Act
        var dto1 = new CrewDto
        {
            Id = id,
            Name = name,
            Description = description,
            ProcessType = processType,
            Status = status,
            Verbosity = verbosity,
            CreatedAt = createdAt
        };

        var dto2 = new CrewDto
        {
            Id = id,
            Name = name,
            Description = description,
            ProcessType = processType,
            Status = status,
            Verbosity = verbosity,
            CreatedAt = createdAt
        };

        // Assert
        Assert.Equal(dto1, dto2);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var dto1 = new CrewDto
        {
            Id = CrewId1,
            Name = "Crew 1",
            Description = "First crew",
            ProcessType = "Sequential",
            Status = "Ready",
            Verbosity = "Normal"
        };

        var dto2 = new CrewDto
        {
            Id = CrewId2,
            Name = "Crew 2",
            Description = "Second crew",
            ProcessType = "Parallel",
            Status = "Executing",
            Verbosity = "Debug"
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

}
