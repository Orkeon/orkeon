using Orkeon.Application.Memory.DTOs;
using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Application.Tests.DTOs.Memory;

public class MemorySearchFilterTests
{
    [Fact]
    public void ShouldDefaultToNull_WhenCreatingEmptyFilter()
    {
        // Act
        var filter = new MemorySearchFilter();

        // Assert
        Assert.Null(filter.AgentId);
        Assert.Null(filter.TaskId);
        Assert.Null(filter.CrewId);
        Assert.Null(filter.MemoryType);
        Assert.Null(filter.After);
        Assert.Null(filter.Before);
        Assert.Null(filter.TopK);
        Assert.Null(filter.MinScore);
        Assert.Null(filter.Tags);
        Assert.Null(filter.Extensions);
    }

    [Fact]
    public void ShouldPopulateAllFields_WhenUsingInitSyntax()
    {
        // Arrange
        var after = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var filter = new MemorySearchFilter
        {
            AgentId = AgentId1,
            TaskId = TaskId1,
            CrewId = CrewId1,
            MemoryType = MemoryType.ShortTerm,
            After = after,
            Before = before,
            TopK = 5,
            MinScore = 0.8,
            Tags = new Dictionary<string, string> { ["env"] = "prod" },
            Extensions = new Dictionary<string, object> { ["custom"] = 42 }
        };

        // Assert
        Assert.Equal(AgentId1, filter.AgentId);
        Assert.Equal(TaskId1, filter.TaskId);
        Assert.Equal(CrewId1, filter.CrewId);
        Assert.Equal(MemoryType.ShortTerm, filter.MemoryType);
        Assert.Equal(after, filter.After);
        Assert.Equal(before, filter.Before);
        Assert.Equal(5, filter.TopK);
        Assert.Equal(0.8, filter.MinScore);
        Assert.Single(filter.Tags!);
        Assert.Single(filter.Extensions!);
    }

    [Theory]
    [InlineData(MemoryType.ShortTerm)]
    [InlineData(MemoryType.LongTerm)]
    [InlineData(MemoryType.Episodic)]
    [InlineData(MemoryType.Entity)]
    public void ShouldAcceptAllMemoryTypes_WhenSettingFilter(MemoryType memoryType)
    {
        // Act
        var filter = new MemorySearchFilter { MemoryType = memoryType };

        // Assert
        Assert.Equal(memoryType, filter.MemoryType);
    }

    [Fact]
    public void ShouldSupportRecordEquality_WhenComparingFilters()
    {
        // Arrange
        var f1 = new MemorySearchFilter { AgentId = "a", TopK = 10 };
        var f2 = new MemorySearchFilter { AgentId = "a", TopK = 10 };

        // Assert
        Assert.Equal(f1, f2);
    }

    [Fact]
    public void ShouldSupportRecordWithExpression_WhenCopying()
    {
        // Arrange
        var original = new MemorySearchFilter { AgentId = "a", TopK = 10, MinScore = 0.5 };

        // Act
        var modified = original with { MinScore = 0.9 };

        // Assert
        Assert.Equal("a", modified.AgentId);
        Assert.Equal(10, modified.TopK);
        Assert.Equal(0.9, modified.MinScore);
    }
}
