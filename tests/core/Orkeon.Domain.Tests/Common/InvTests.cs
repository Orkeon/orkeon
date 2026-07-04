using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for the Inv (invariant culture) formatting and parsing utilities.
/// Ensures consistent number and date formatting regardless of system locale.
/// </summary>
public class InvTests
{
    // ── Format: double ───────────────────────────────────────────────

    [Theory]
    [InlineData(3.14159, "F2", "3.14")]
    [InlineData(1234.5, "N2", "1,234.50")]
    [InlineData(0.0, "F4", "0.0000")]
    [InlineData(-99.99, "F1", "-100.0")]
    public void ShouldFormatDouble_WhenUsingInvariantCulture(double value, string format, string expected)
    {
        // Act
        var result = Inv.ToString(value, format);

        // Assert
        Assert.Equal(expected, result);
    }

    // ── Format: float ────────────────────────────────────────────────

    [Theory]
    [InlineData(2.5f, "F1", "2.5")]
    [InlineData(1000.0f, "N0", "1,000")]
    [InlineData(-0.001f, "F4", "-0.0010")]
    public void ShouldFormatFloat_WhenUsingInvariantCulture(float value, string format, string expected)
    {
        // Act
        var result = Inv.ToString(value, format);

        // Assert
        Assert.Equal(expected, result);
    }

    // ── Format: decimal ──────────────────────────────────────────────

    [Fact]
    public void ShouldFormatDecimal_WhenUsingInvariantCulture()
    {
        // Arrange
        var value = 12345.6789m;

        // Act
        var result = Inv.ToString(value, "N2");

        // Assert
        Assert.Equal("12,345.68", result);
    }

    [Fact]
    public void ShouldFormatDecimalWithFixedPrecision_WhenUsingF4()
    {
        // Arrange
        var value = 1.1m;

        // Act
        var result = Inv.ToString(value, "F4");

        // Assert
        Assert.Equal("1.1000", result);
    }

    // ── Format: DateTime ─────────────────────────────────────────────

    [Fact]
    public void ShouldFormatDateTime_WhenUsingInvariantCulture()
    {
        // Arrange
        var dt = new DateTime(2026, 3, 28, 14, 30, 0, DateTimeKind.Utc);

        // Act
        var result = Inv.ToString(dt, "yyyy-MM-dd HH:mm:ss");

        // Assert
        Assert.Equal("2026-03-28 14:30:00", result);
    }

    // ── Format: DateTimeOffset ───────────────────────────────────────

    [Fact]
    public void ShouldFormatDateTimeOffset_WhenUsingInvariantCulture()
    {
        // Arrange
        var dto = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.FromHours(2));

        // Act
        var result = Inv.ToString(dto, "yyyy-MM-ddTHH:mm:sszzz");

        // Assert
        Assert.Equal("2026-01-15T10:00:00+02:00", result);
    }

    // ── ParseDouble ──────────────────────────────────────────────────

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("-0.001", -0.001)]
    [InlineData("1E+3", 1000.0)]
    public void ShouldParseDouble_WhenGivenInvariantString(string input, double expected)
    {
        // Act
        var result = Inv.ParseDouble(input);

        // Assert
        Assert.Equal(expected, result, precision: 10);
    }

    [Fact]
    public void ShouldThrowFormatException_WhenParsingInvalidDouble()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => Inv.ParseDouble("not_a_number"));
    }

    // ── TryParseDouble ───────────────────────────────────────────────

    [Theory]
    [InlineData("42.5", true, 42.5)]
    [InlineData("abc", false, 0.0)]
    [InlineData("1,000", true, 1000.0)]
    public void ShouldTryParseDouble_WhenGivenVariousInputs(string input, bool expectedSuccess, double expectedValue)
    {
        // Act
        var success = Inv.TryParseDouble(input, out var result);

        // Assert
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
            Assert.Equal(expectedValue, result, precision: 10);
    }

    // ── ParseFloat ───────────────────────────────────────────────────

    [Theory]
    [InlineData("2.5", 2.5f)]
    [InlineData("-100.0", -100.0f)]
    [InlineData("1E+2", 100.0f)]
    public void ShouldParseFloat_WhenGivenInvariantString(string input, float expected)
    {
        // Act
        var result = Inv.ParseFloat(input);

        // Assert
        Assert.Equal(expected, result, precision: 5);
    }

    [Fact]
    public void ShouldThrowFormatException_WhenParsingInvalidFloat()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => Inv.ParseFloat("not_a_number"));
    }

    // ── TryParseFloat ────────────────────────────────────────────────

    [Theory]
    [InlineData("3.14", true, 3.14f)]
    [InlineData("xyz", false, 0.0f)]
    public void ShouldTryParseFloat_WhenGivenVariousInputs(string input, bool expectedSuccess, float expectedValue)
    {
        // Act
        var success = Inv.TryParseFloat(input, out var result);

        // Assert
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
            Assert.Equal(expectedValue, result, precision: 5);
    }

    // ── ParseDecimal ─────────────────────────────────────────────────

    [Theory]
    [InlineData("12345.6789", "12345.6789")]
    [InlineData("1,000.50", "1000.50")]
    [InlineData("-99.99", "-99.99")]
    public void ShouldParseDecimal_WhenGivenInvariantString(string input, string expectedStr)
    {
        // Arrange
        var expected = decimal.Parse(expectedStr, CultureInfo.InvariantCulture);

        // Act
        var result = Inv.ParseDecimal(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldThrowFormatException_WhenParsingInvalidDecimal()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => Inv.ParseDecimal("not_a_number"));
    }

    // ── TryParseDecimal ──────────────────────────────────────────────

    [Theory]
    [InlineData("99.99", true, "99.99")]
    [InlineData("bad", false, "0")]
    public void ShouldTryParseDecimal_WhenGivenVariousInputs(string input, bool expectedSuccess, string expectedStr)
    {
        // Arrange
        var expectedValue = decimal.Parse(expectedStr, CultureInfo.InvariantCulture);

        // Act
        var success = Inv.TryParseDecimal(input, out var result);

        // Assert
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
            Assert.Equal(expectedValue, result);
    }

    // ── ConvertTo: Double ────────────────────────────────────────────

    [Theory]
    [InlineData(42, 42.0)]
    [InlineData(3.14f, 3.14)]
    [InlineData("99.5", 99.5)]
    public void ShouldConvertToDouble_WhenGivenVariousTypes(object input, double expected)
    {
        // Act
        var result = Inv.ToDouble(input);

        // Assert
        Assert.Equal(expected, result, precision: 5);
    }

    // ── ConvertTo: Float ─────────────────────────────────────────────

    [Theory]
    [InlineData(42, 42.0f)]
    [InlineData(2.5, 2.5f)]
    [InlineData("7.5", 7.5f)]
    public void ShouldConvertToFloat_WhenGivenVariousTypes(object input, float expected)
    {
        // Act
        var result = Inv.ToFloat(input);

        // Assert
        Assert.Equal(expected, result, precision: 5);
    }

    // ── ConvertTo: Decimal ───────────────────────────────────────────

    [Fact]
    public void ShouldConvertToDecimal_WhenGivenInt()
    {
        // Act
        var result = Inv.ToDecimal(42);

        // Assert
        Assert.Equal(42m, result);
    }

    [Fact]
    public void ShouldConvertToDecimal_WhenGivenString()
    {
        // Act
        var result = Inv.ToDecimal("123.45");

        // Assert
        Assert.Equal(123.45m, result);
    }

    [Fact]
    public void ShouldConvertToDecimal_WhenGivenDouble()
    {
        // Act
        var result = Inv.ToDecimal(9.99);

        // Assert
        Assert.Equal(9.99m, result);
    }

    // ── Format(FormattableString) ────────────────────────────────────

    [Fact]
    public void ShouldFormatInterpolatedString_WhenUsingInvariantCulture()
    {
        // Arrange
        double pi = 3.14159;

        // Act
        var result = Inv.Format($"Pi is {pi:F2}");

        // Assert
        Assert.Equal("Pi is 3.14", result);
    }

    [Fact]
    public void ShouldFormatInterpolatedStringWithMultipleValues_WhenUsingInvariantCulture()
    {
        // Arrange
        double price = 1234.50;
        int qty = 3;

        // Act
        var result = Inv.Format($"Price: {price:N2}, Qty: {qty}");

        // Assert
        Assert.Equal("Price: 1,234.50, Qty: 3", result);
    }

    // ── Culture field ────────────────────────────────────────────────

    [Fact]
    public void ShouldExposeInvariantCultureProvider()
    {
        // Assert
        Assert.NotNull(Inv.Culture);
        Assert.Same(CultureInfo.InvariantCulture, Inv.Culture);
    }
}
