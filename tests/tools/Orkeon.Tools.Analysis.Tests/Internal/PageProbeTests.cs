using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis.Tests.Internal;

public sealed class PageProbeTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(200, 201)]
    public void ShouldAskOnePastThePage_WhenPageIsBelowMaxValue(int pageSize, int expected)
    {
        Assert.Equal(expected, PageProbe.OnePast(pageSize));
    }

    [Fact]
    public void ShouldHoldAtMaxValue_WhenPageIsMaxValue()
    {
        // A page of int.MaxValue would overflow to int.MinValue and ask the store for nothing.
        Assert.Equal(int.MaxValue, PageProbe.OnePast(int.MaxValue));
    }
}
