using Orkeon.Domain.Tools;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ToolCallOptions following Clean Architecture principles.
/// Tests the business rules and validation logic of the ToolCallOptions class.
/// </summary>
public class ToolCallOptionsTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var options = ToolCallOptions.Empty;

        // Assert
        Assert.NotNull(options);
        Assert.False(options.Contains("any-key"));
        Assert.Null(options.Get<string>("non-existent"));
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenUsingEmpty()
    {
        // Act
        var options1 = ToolCallOptions.Empty;
        var options2 = ToolCallOptions.Empty;

        // Assert
        Assert.Same(options1, options2);
    }

    [Fact]
    public void ShouldCreateOptionsWithDefaults_WhenUsingDefault()
    {
        // Act
        var options = ToolCallOptions.Default();

        // Assert
        Assert.NotNull(options);
        Assert.True(options.Contains("timeout"));
        Assert.True(options.Contains("retryCount"));
        Assert.True(options.Contains("parallel"));

        Assert.Equal(TimeoutQuick, options.Get<TimeSpan>("timeout"));
        Assert.Equal(3, options.Get<int>("retryCount"));
        Assert.False(options.Get<bool>("parallel"));
    }

    [Fact]
    public void ShouldCreateParallelOptions_WhenUsingForParallelExecution()
    {
        // Act
        var options = ToolCallOptions.ForParallelExecution();

        // Assert
        Assert.NotNull(options);
        Assert.True(options.Get<bool>("parallel"));
        Assert.Equal(5, options.Get<int>("maxConcurrency"));
        Assert.Equal(TimeSpan.FromSeconds(60), options.Get<TimeSpan>("timeout"));
    }

    [Fact]
    public void ShouldUseProvidedValue_WhenUsingForParallelExecutionWithCustomConcurrency()
    {
        // Arrange
        var maxConcurrency = 10;

        // Act
        var options = ToolCallOptions.ForParallelExecution(maxConcurrency);

        // Assert
        Assert.Equal(maxConcurrency, options.Get<int>("maxConcurrency"));
    }

    [Fact]
    public void ShouldAddTimeoutOption_WhenUsingBuilderAddTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromMinutes(2);

        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddTimeout(timeout)
            .Build();

        // Assert
        Assert.Equal(timeout, options.Get<TimeSpan>("timeout"));
    }

    [Fact]
    public void ShouldAddRetryOption_WhenUsingBuilderAddRetryCount()
    {
        // Arrange
        var retryCount = 5;

        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddRetryCount(retryCount)
            .Build();

        // Assert
        Assert.Equal(retryCount, options.Get<int>("retryCount"));
    }

    [Fact]
    public void ShouldAddParallelOption_WhenUsingBuilderAddParallel()
    {
        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddParallel(true)
            .Build();

        // Assert
        Assert.True(options.Get<bool>("parallel"));
    }

    [Fact]
    public void ShouldAddConcurrencyOption_WhenUsingBuilderAddMaxConcurrency()
    {
        // Arrange
        var maxConcurrency = 8;

        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddMaxConcurrency(maxConcurrency)
            .Build();

        // Assert
        Assert.Equal(maxConcurrency, options.Get<int>("maxConcurrency"));
    }

    [Fact]
    public void ShouldAddValidationOption_WhenUsingBuilderAddValidation()
    {
        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddValidation(true)
            .Build();

        // Assert
        Assert.True(options.Get<bool>("validateArgs"));
    }

    [Fact]
    public void ShouldAddTracingOption_WhenUsingBuilderAddTracing()
    {
        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddTracing(true)
            .Build();

        // Assert
        Assert.True(options.Get<bool>("tracing"));
    }

    [Fact]
    public void ShouldAddCustomOption_WhenUsingBuilderAdd()
    {
        // Arrange
        var customValue = "custom-value";

        // Act
        var options = ToolCallOptions.CreateBuilder()
            .Add("customKey", customValue)
            .Build();

        // Assert
        Assert.True(options.Contains("customKey"));
        Assert.Equal(customValue, options.Get<string>("customKey"));
    }

    [Fact]
    public void ShouldCreateCompleteOptions_WhenUsingBuilderChainedCalls()
    {
        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddTimeout(TimeSpan.FromMinutes(1))
            .AddRetryCount(2)
            .AddParallel(true)
            .AddMaxConcurrency(4)
            .AddValidation(true)
            .AddTracing(false)
            .Add("customFlag", "enabled")
            .Build();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(1), options.Get<TimeSpan>("timeout"));
        Assert.Equal(2, options.Get<int>("retryCount"));
        Assert.True(options.Get<bool>("parallel"));
        Assert.Equal(4, options.Get<int>("maxConcurrency"));
        Assert.True(options.Get<bool>("validateArgs"));
        Assert.False(options.Get<bool>("tracing"));
        Assert.Equal("enabled", options.Get<string>("customFlag"));
    }

    [Fact]
    public void ShouldReplaceValue_WhenUsingBuilderOverwriteKey()
    {
        // Act
        var options = ToolCallOptions.CreateBuilder()
            .AddRetryCount(3)
            .AddRetryCount(5) // Override
            .Build();

        // Assert
        Assert.Equal(5, options.Get<int>("retryCount"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var options = ToolCallOptions.Empty;

        // Act & Assert
        Assert.Null(options.Get<string>("nonExistent"));
        Assert.Equal(0, options.Get<int>("nonExistent"));
        Assert.False(options.Get<bool>("nonExistent"));
        Assert.Equal(TimeSpan.Zero, options.Get<TimeSpan>("nonExistent"));
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContains()
    {
        // Arrange
        var options = ToolCallOptions.CreateBuilder()
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

        // Act & Assert
        Assert.True(options.Contains("timeout"));
        Assert.False(options.Contains("nonExistent"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToolCallOptionValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ToolCallOptionValue.From(null!));
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingToolCallOptionValueGettingValueWithSameType()
    {
        // Arrange
        var value = ToolCallOptionValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingToolCallOptionValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = ToolCallOptionValue.From(42);

        // Act
        var result = value.GetValue<double>();

        // Assert
        Assert.Equal(42.0, result);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingToolCallOptionValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = ToolCallOptionValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert tool call option value", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenUsingToolCallOptionValueUsingProperties()
    {
        // Arrange
        var input = TimeoutStandard;
        var value = ToolCallOptionValue.From(input);

        // Act & Assert
        Assert.Equal(input, value.RawValue);
        Assert.Equal(typeof(TimeSpan), value.ValueType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(42)]
    [InlineData("string value")]
    [InlineData(3.14)]
    public void ShouldStoreCorrectly_WhenUsingToolCallOptionValueFromWithVariousTypes(object input)
    {
        // Act
        var value = ToolCallOptionValue.From(input);

        // Assert
        Assert.Equal(input, value.RawValue);
        Assert.Equal(input.GetType(), value.ValueType);
    }

    [Fact]
    public void ShouldStoreObject_WhenUsingBuilderAddComplexObject()
    {
        // Arrange
        var complexObject = new { Name = "Test", Count = 5 };

        // Act
        var options = ToolCallOptions.CreateBuilder()
            .Add("complex", complexObject)
            .Build();

        // Assert
        var retrieved = options.Get<object>("complex");
        Assert.NotNull(retrieved);
        Assert.Equal(complexObject, retrieved);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenGettingWithNullableTypes()
    {
        // Arrange
        var options = ToolCallOptions.CreateBuilder()
            .Add("nullableInt", 42)
            .Build();

        // Act
        var result = options.Get<int?>("nullableInt");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldWork_WhenUsingTypeConversionIntToString()
    {
        // Arrange
        var options = ToolCallOptions.CreateBuilder()
            .AddRetryCount(3)
            .Build();

        // Act
        var stringValue = options.Get<string>("retryCount");

        // Assert
        Assert.Equal("3", stringValue);
    }
}
