using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TaskStatusTests
{
    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldHaveFiveValues_WhenListingAll()
    {
        Assert.Equal(5, TaskStatus.All.Count);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldRefuseBlocked_WhenParsingIt()
    {
        Assert.False(TaskStatus.TryFrom("Blocked", out _));
    }

    [Fact]
    public void ShouldHaveCorrectNames_WhenUsingToString()
    {
        Assert.Equal(Pending, TaskStatus.Pending.ToString());
        Assert.Equal(InProgress, TaskStatus.InProgress.ToString());
        Assert.Equal(Completed, TaskStatus.Completed.ToString());
        Assert.Equal(Failed, TaskStatus.Failed.ToString());
        Assert.Equal("Cancelled", TaskStatus.Cancelled.ToString());
    }

    [Theory]
    [InlineData(Pending)]
    [InlineData(InProgress)]
    [InlineData(Completed)]
    [InlineData(Failed)]
    [InlineData("Cancelled")]
    public void ShouldParse_WhenUsingFromWithValidString(string value)
    {
        var status = TaskStatus.From(value);
        Assert.Equal(value, status.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidString()
    {
        Assert.Throws<ArgumentException>(() => TaskStatus.From("Invalid"));
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingImplicitConversion()
    {
        string value = TaskStatus.Pending;
        Assert.Equal(Pending, value);
    }
}
