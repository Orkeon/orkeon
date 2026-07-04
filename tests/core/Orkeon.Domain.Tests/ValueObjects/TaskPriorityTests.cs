using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TaskPriorityTests
{
    [Fact]
    public void ShouldHaveExpectedCount_WhenGettingAll()
    {
        Assert.Equal(5, TaskPriority.All.Count);
    }

    [Fact]
    public void ShouldHaveCorrectOrder_WhenUsingOrderProperty()
    {
        Assert.Equal(1, TaskPriority.Low.Order);
        Assert.Equal(2, TaskPriority.Normal.Order);
        Assert.Equal(3, TaskPriority.High.Order);
        Assert.Equal(4, TaskPriority.Critical.Order);
        Assert.Equal(5, TaskPriority.Urgent.Order);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Normal")]
    [InlineData("High")]
    [InlineData("Critical")]
    [InlineData("Urgent")]
    public void ShouldParse_WhenUsingFromWithValidString(string value)
    {
        var priority = TaskPriority.From(value);
        Assert.Equal(value, priority.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidString()
    {
        Assert.Throws<ArgumentException>(() => TaskPriority.From("Invalid"));
    }

    [Fact]
    public void ShouldBeOrderedByImportance_WhenSortingByOrder()
    {
        var priorities = new[] { TaskPriority.Urgent, TaskPriority.Critical, TaskPriority.Low, TaskPriority.High, TaskPriority.Normal };
        var sorted = priorities.OrderBy(p => p.Order).ToArray();
        Assert.Equal(TaskPriority.Low, sorted[0]);
        Assert.Equal(TaskPriority.Normal, sorted[1]);
        Assert.Equal(TaskPriority.High, sorted[2]);
        Assert.Equal(TaskPriority.Critical, sorted[3]);
        Assert.Equal(TaskPriority.Urgent, sorted[4]);
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingImplicitConversion()
    {
        string value = TaskPriority.High;
        Assert.Equal("High", value);
    }
}
