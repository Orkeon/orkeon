using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

/// <summary>
/// An "integer" parameter accepts a whole number whatever CLR type carries it. A script's
/// numbers all arrive as doubles (Jint maps every JS number to System.Double), so a
/// `tools.emailSearch({ limit: 20 })` used to be refused as "invalid type. Expected: integer".
/// </summary>
public class ToolParameterValidatorIntegerTests
{
    [Theory]
    [InlineData(20.0)]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void A_whole_double_is_an_integer(double value) =>
        Assert.True(ToolParameterValidator.IsValidType(value, "integer"));

    [Theory]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e300)]
    public void A_fractional_or_non_finite_or_out_of_range_double_is_not(double value) =>
        Assert.False(ToolParameterValidator.IsValidType(value, "integer"));

    [Fact]
    public void Whole_float_and_decimal_values_are_integers_and_fractional_ones_are_not()
    {
        Assert.True(ToolParameterValidator.IsValidType(7f, "integer"));
        Assert.False(ToolParameterValidator.IsValidType(7.25f, "integer"));
        Assert.True(ToolParameterValidator.IsValidType(12m, "integer"));
        Assert.False(ToolParameterValidator.IsValidType(12.5m, "integer"));
    }

    [Fact]
    public void Every_integral_clr_type_is_an_integer()
    {
        Assert.True(ToolParameterValidator.IsValidType(3, "integer"));
        Assert.True(ToolParameterValidator.IsValidType(3L, "integer"));
        Assert.True(ToolParameterValidator.IsValidType((short)3, "integer"));
        Assert.True(ToolParameterValidator.IsValidType((byte)3, "integer"));
        Assert.True(ToolParameterValidator.IsValidType(3u, "integer"));
        Assert.True(ToolParameterValidator.IsValidType("42", "integer"));
        Assert.False(ToolParameterValidator.IsValidType("4.2", "integer"));
    }
}
