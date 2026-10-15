using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Tests.DTOs.Common;

public class MemoryItemDtoTests
{
    private static readonly string[] s_importantVerified = ["important", "verified"];

    [Fact]
    public void ShouldCreateValidDto_WhenUsingMemoryItemDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new MemoryItemDto
        {
            Id = "mem-123",
            Content = "Important memory content",
            Type = "fact"
        };

        // Assert
        Assert.Equal("mem-123", dto.Id);
        Assert.Equal("Important memory content", dto.Content);
        Assert.Equal("fact", dto.Type);
        Assert.Equal(0.0, dto.Relevance);
        Assert.Equal(default(DateTime), dto.CreatedAt);
        Assert.Null(dto.AgentId);
        Assert.Null(dto.TaskId);
        Assert.Empty(dto.Metadata);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingMemoryItemDtoWithAllProperties()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "source", "user_input" },
            { "confidence", 0.95 },
            { "tags", s_importantVerified }
        };

        var createdAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var dto = new MemoryItemDto
        {
            Id = "mem-complete",
            Content = "Complete memory item with all properties",
            Type = "experience",
            Relevance = 0.92,
            CreatedAt = createdAt,
            AgentId = "agent-789",
            TaskId = "task-456",
            Metadata = metadata
        };

        // Assert
        Assert.Equal("mem-complete", dto.Id);
        Assert.Equal("experience", dto.Type);
        Assert.Equal(0.92, dto.Relevance);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal("agent-789", dto.AgentId);
        Assert.Equal("task-456", dto.TaskId);
        Assert.Equal(3, dto.Metadata.Count);
        Assert.Equal("user_input", dto.Metadata["source"]);
        Assert.Equal(0.95, dto.Metadata["confidence"]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ShouldAcceptValidRange_WhenUsingMemoryItemDtoUsingRelevanceScore(double relevance)
    {
        // Arrange & Act
        var dto = new MemoryItemDto
        {
            Id = "mem-1",
            Content = "Content",
            Type = "fact",
            Relevance = relevance
        };

        // Assert
        Assert.Equal(relevance, dto.Relevance);
    }
}
