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

    [Fact]
    public void ShouldReturnCorrectValue_WhenCheckingIsDefault()
    {
#pragma warning disable CS1718 // Testing reflexive equality
        Assert.True(ProcessType.Sequential == ProcessType.Sequential);
#pragma warning restore CS1718
        Assert.False(ProcessType.Parallel == ProcessType.Sequential);
    }
}
