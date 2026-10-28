using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Task;

public class ThresholdExpressionTests
{
    [Theory]
    [Trait("BR", "BR-03")]
    [InlineData(">= 0.7", ConditionComparison.GreaterOrEqual, 0.7)]
    [InlineData("> 1.5", ConditionComparison.Greater, 1.5)]
    [InlineData("<= 0.2", ConditionComparison.LessOrEqual, 0.2)]
    [InlineData("  < 1  ", ConditionComparison.Less, 1.0)]
    public void ShouldParse_WhenOperatorAndFiniteNumber(string text, ConditionComparison comparison, double value)
    {
        Assert.True(ThresholdExpression.TryParse(text, out var parsedComparison, out var parsedValue));
        Assert.Equal(comparison, parsedComparison);
        Assert.Equal(value, parsedValue);
    }

    [Theory]
    [Trait("BR", "BR-03")]
    [InlineData("> NaN")]
    [InlineData("> Infinity")]
    [InlineData(">= -Infinity")]
    public void ShouldRefuse_WhenNumberNotFinite(string text)
    {
        Assert.False(ThresholdExpression.TryParse(text, out _, out _));
    }

    [Theory]
    [Trait("BR", "BR-03")]
    [InlineData("")]
    [InlineData("0.7")]
    [InlineData("== 0.7")]
    [InlineData("> high")]
    [InlineData("garbage")]
    public void ShouldRefuse_WhenNotOperatorFollowedByNumber(string text)
    {
        Assert.False(ThresholdExpression.TryParse(text, out _, out _));
    }
}
