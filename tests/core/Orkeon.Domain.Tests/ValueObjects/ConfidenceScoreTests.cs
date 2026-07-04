using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ConfidenceScoreTests
{
    #region Construction Tests

    [Fact]
    public void ShouldCreateScore_WhenUsingFromWithValidValue()
    {
        // Act
        var score = ConfidenceScore.From(0.85);

        // Assert
        Assert.Equal(0.85, score.Value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.99)]
    [InlineData(1.0)]
    public void ShouldAcceptValue_WhenUsingFromWithValueInRange(double value)
    {
        // Act
        var score = ConfidenceScore.From(value);

        // Assert
        Assert.Equal(value, score.Value);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenUsingFromWithValueBelow0()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ConfidenceScore.From(-0.1));
        Assert.Contains("Confidence score must be between 0.0 and 1.0", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenUsingFromWithValueAbove1()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ConfidenceScore.From(1.1));
        Assert.Contains("Confidence score must be between 0.0 and 1.0", exception.Message);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    [InlineData(100.0)]
    public void ShouldThrowArgumentOutOfRangeException_WhenUsingFromWithValueOutOfRange(double value)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceScore.From(value));
    }

    #endregion

    #region Degrade Tests

    [Fact]
    public void ShouldReduceValue_WhenDegradingWithDefaultFactor()
    {
        // Arrange
        var score = ConfidenceScore.From(1.0);

        // Act
        var degraded = score.Degrade();

        // Assert
        Assert.Equal(0.9, degraded.Value, precision: 10);
    }

    [Fact]
    public void ShouldReduceValueByCustomFactor_WhenDegradingWithCustomFactor()
    {
        // Arrange
        var score = ConfidenceScore.From(0.8);

        // Act
        var degraded = score.Degrade(0.5);

        // Assert
        Assert.Equal(0.4, degraded.Value, precision: 10);
    }

    [Fact]
    public void ShouldReturnZero_WhenDegradingWithZeroFactor()
    {
        // Arrange
        var score = ConfidenceScore.From(0.8);

        // Act
        var degraded = score.Degrade(0.0);

        // Assert
        Assert.Equal(0.0, degraded.Value);
    }

    [Fact]
    public void ShouldReturnSameValue_WhenDegradingWithFactorOf1()
    {
        // Arrange
        var score = ConfidenceScore.From(0.75);

        // Act
        var degraded = score.Degrade(1.0);

        // Assert
        Assert.Equal(0.75, degraded.Value);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenDegradingWithFactorBelow0()
    {
        // Arrange
        var score = ConfidenceScore.From(0.5);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => score.Degrade(-0.1));
        Assert.Contains("Degradation factor must be between 0.0 and 1.0", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenDegradingWithFactorAbove1()
    {
        // Arrange
        var score = ConfidenceScore.From(0.5);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => score.Degrade(1.1));
        Assert.Contains("Degradation factor must be between 0.0 and 1.0", exception.Message);
    }

    [Fact]
    public void ShouldCompoundCorrectly_WhenDegradingMultipleTimes()
    {
        // Arrange
        var score = ConfidenceScore.From(1.0);

        // Act - Degrade 3 times with default factor
        var degraded = score.Degrade().Degrade().Degrade();

        // Assert: 1.0 * 0.9 * 0.9 * 0.9 = 0.729
        Assert.Equal(0.729, degraded.Value, precision: 10);
    }

    [Fact]
    public void ShouldNotMutateOriginal_WhenDegrading()
    {
        // Arrange
        var original = ConfidenceScore.From(0.8);

        // Act
        var degraded = original.Degrade();

        // Assert
        Assert.Equal(0.8, original.Value);
        Assert.Equal(0.72, degraded.Value, precision: 10);
    }

    #endregion

    #region DefaultDegradationFactor Tests

    [Fact]
    public void ShouldBe0Point9_WhenCheckingDefaultDegradationFactor()
    {
        // Assert
        Assert.Equal(0.9, ConfidenceScore.DefaultDegradationFactor);
    }

    #endregion

    #region Implicit Conversion Tests

    [Fact]
    public void ShouldConvertToDouble_WhenUsingImplicitConversion()
    {
        // Arrange
        var score = ConfidenceScore.From(0.75);

        // Act
        double value = score;

        // Assert
        Assert.Equal(0.75, value);
    }

    [Fact]
    public void ShouldWorkInArithmetic_WhenUsingImplicitConversion()
    {
        // Arrange
        var score = ConfidenceScore.From(0.5);

        // Act
        double result = score * 2.0;

        // Assert
        Assert.Equal(1.0, result);
    }

    #endregion

    #region Explicit Conversion Tests

    [Fact]
    public void ShouldConvertFromDouble_WhenUsingExplicitConversion()
    {
        // Act
        var score = (ConfidenceScore)0.65;

        // Assert
        Assert.Equal(0.65, score.Value);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenExplicitConversionOutOfRange()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => (ConfidenceScore)1.5);
    }

    #endregion

    #region Equality Tests (Record)

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoScoresWithSameValue()
    {
        // Arrange
        var score1 = ConfidenceScore.From(0.75);
        var score2 = ConfidenceScore.From(0.75);

        // Act & Assert
        Assert.Equal(score1, score2);
        Assert.True(score1 == score2);
        Assert.False(score1 != score2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoScoresWithDifferentValues()
    {
        // Arrange
        var score1 = ConfidenceScore.From(0.5);
        var score2 = ConfidenceScore.From(0.75);

        // Act & Assert
        Assert.NotEqual(score1, score2);
        Assert.True(score1 != score2);
        Assert.False(score1 == score2);
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoScoresAreEqual()
    {
        // Arrange
        var score1 = ConfidenceScore.From(0.5);
        var score2 = ConfidenceScore.From(0.5);

        // Act & Assert
        Assert.Equal(score1.GetHashCode(), score2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoScoresAreDifferent()
    {
        // Arrange
        var score1 = ConfidenceScore.From(0.3);
        var score2 = ConfidenceScore.From(0.7);

        // Act & Assert
        Assert.NotEqual(score1.GetHashCode(), score2.GetHashCode());
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var score = ConfidenceScore.From(0.75);

        // Act
        var result = score.ToString();

        // Assert
        Assert.Equal("0.75", result);
    }

    [Fact]
    public void ShouldReturnFormattedStringWithTwoDecimals_WhenCallingToStringOnInteger()
    {
        // Arrange
        var score = ConfidenceScore.From(1.0);

        // Act
        var result = score.ToString();

        // Assert
        Assert.Equal("1.00", result);
    }

    [Fact]
    public void ShouldReturnFormattedStringForZero_WhenCallingToString()
    {
        // Arrange
        var score = ConfidenceScore.From(0.0);

        // Act
        var result = score.ToString();

        // Assert
        Assert.Equal("0.00", result);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void ShouldAcceptExactly0_WhenUsingFromWith0()
    {
        // Act
        var score = ConfidenceScore.From(0.0);

        // Assert
        Assert.Equal(0.0, score.Value);
    }

    [Fact]
    public void ShouldAcceptExactly1_WhenUsingFromWith1()
    {
        // Act
        var score = ConfidenceScore.From(1.0);

        // Assert
        Assert.Equal(1.0, score.Value);
    }

    [Fact]
    public void ShouldStayAtZero_WhenDegradingFromZero()
    {
        // Arrange
        var score = ConfidenceScore.From(0.0);

        // Act
        var degraded = score.Degrade();

        // Assert
        Assert.Equal(0.0, degraded.Value);
    }

    #endregion
}
