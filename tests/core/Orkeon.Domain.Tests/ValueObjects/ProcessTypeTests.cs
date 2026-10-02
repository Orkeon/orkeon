using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ProcessTypeTests
{
    [Fact]
    public void ShouldHaveExpectedCount_WhenGettingAll()
    {
        Assert.Equal(6, ProcessType.All.Count);
    }

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    [InlineData("Parallel")]
    [InlineData("Graph")]
    [InlineData("Autonomous")]
    public void ShouldParse_WhenUsingFromWithValidString(string value)
    {
        var processType = ProcessType.From(value);
        Assert.Equal(value, processType.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidString()
    {
        Assert.Throws<ArgumentException>(() => ProcessType.From("Invalid"));
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingImplicitConversion()
    {
        string value = ProcessType.Sequential;
        Assert.Equal("Sequential", value);
    }

    /// <summary>
    /// GAP-22 — Sequential honours a task's asyncExecution and Parallel accepts it (a wave already runs
    /// at once); the four modes that order their tasks themselves do not accept it.
    /// </summary>
    [Theory]
    [InlineData("Sequential", true)]
    [InlineData("Parallel", true)]
    [InlineData("Hierarchical", false)]
    [InlineData("Consensual", false)]
    [InlineData("Graph", false)]
    [InlineData("Autonomous", false)]
    public void Only_sequential_and_parallel_accept_async_execution(string value, bool accepts)
    {
        Assert.Equal(accepts, ProcessType.From(value).AcceptsAsyncExecution);
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenCheckingIsDefault()
    {
#pragma warning disable CS1718 // Testing reflexive equality
        Assert.True(ProcessType.Sequential == ProcessType.Sequential);
#pragma warning restore CS1718
        Assert.False(ProcessType.Parallel == ProcessType.Sequential);
    }
}
