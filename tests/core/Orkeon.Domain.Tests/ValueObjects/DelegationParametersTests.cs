using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class DelegationParametersTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var parameters = DelegationParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.False(parameters.Contains("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var parameters = DelegationParameters.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Add("key3", true)
            .Build();

        // Act & Assert
        Assert.Equal("value1", parameters.Get<string>("key1"));
        Assert.Equal(42, parameters.Get<int>("key2"));
        Assert.True(parameters.Get<bool>("key3"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var parameters = DelegationParameters.Empty;

        // Act & Assert
        Assert.Null(parameters.Get<string>("missing"));
        Assert.Equal(0, parameters.Get<int>("missing"));
        Assert.False(parameters.Get<bool>("missing"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var parameters = DelegationParameters.CreateBuilder()
            .Add("required", "value")
            .Build();

        // Act
        var result = parameters.GetRequired<string>("required");

        // Assert
        Assert.Equal("value", result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var parameters = DelegationParameters.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => parameters.GetRequired<string>("missing"));
        Assert.Contains("Required parameter 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContains()
    {
        // Arrange
        var parameters = DelegationParameters.CreateBuilder()
            .Add("existing", "value")
            .Build();

        // Act & Assert
        Assert.True(parameters.Contains("existing"));
        Assert.False(parameters.Contains("missing"));
    }

    [Fact]
    public void ShouldCreateCorrectParameters_WhenUsingForSkillBasedWithDefaultValue()
    {
        // Act
        var parameters = DelegationParameters.ForSkillBased();

        // Assert
        Assert.Equal(0.8, parameters.Get<double>("minSkillMatch"));
        Assert.Equal("SkillBased", parameters.Get<string>("delegationType"));
    }

    [Fact]
    public void ShouldCreateCorrectParameters_WhenUsingForSkillBasedWithCustomValue()
    {
        // Act
        var parameters = DelegationParameters.ForSkillBased(0.95);

        // Assert
        Assert.Equal(0.95, parameters.Get<double>("minSkillMatch"));
        Assert.Equal("SkillBased", parameters.Get<string>("delegationType"));
    }

    [Fact]
    public void ShouldCreateCorrectParameters_WhenUsingForWorkloadBasedWithDefaultValue()
    {
        // Act
        var parameters = DelegationParameters.ForWorkloadBased();

        // Assert
        Assert.Equal(5, parameters.Get<int>("maxWorkload"));
        Assert.Equal("WorkloadBased", parameters.Get<string>("delegationType"));
    }

    [Fact]
    public void ShouldCreateCorrectParameters_WhenUsingForWorkloadBasedWithCustomValue()
    {
        // Act
        var parameters = DelegationParameters.ForWorkloadBased(10);

        // Assert
        Assert.Equal(10, parameters.Get<int>("maxWorkload"));
        Assert.Equal("WorkloadBased", parameters.Get<string>("delegationType"));
    }

    [Fact]
    public void ShouldCreateCorrectParameters_WhenUsingForHierarchical()
    {
        // Arrange
        var managerRole = "ProjectManager";

        // Act
        var parameters = DelegationParameters.ForHierarchical(managerRole);

        // Assert
        Assert.Equal(managerRole, parameters.Get<string>("managerRole"));
        Assert.Equal("Hierarchical", parameters.Get<string>("delegationType"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddMinSkillMatch()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddMinSkillMatch(0.75)
            .Build();

        // Assert
        Assert.Equal(0.75, parameters.Get<double>("minSkillMatch"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddMaxWorkload()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddMaxWorkload(8)
            .Build();

        // Assert
        Assert.Equal(8, parameters.Get<int>("maxWorkload"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddManagerRole()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddManagerRole("TeamLead")
            .Build();

        // Assert
        Assert.Equal("TeamLead", parameters.Get<string>("managerRole"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddPriority()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddPriority(0.9)
            .Build();

        // Assert
        Assert.Equal(0.9, parameters.Get<double>("priority"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromMinutes(30);

        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddTimeout(timeout)
            .Build();

        // Assert
        Assert.Equal(timeout, parameters.Get<TimeSpan>("timeout"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddRetryLimit()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddRetryLimit(3)
            .Build();

        // Assert
        Assert.Equal(3, parameters.Get<int>("retryLimit"));
    }

    [Fact]
    public void ShouldAddParameter_WhenUsingBuilderAddDelegationType()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddDelegationType(DelegationType.SkillBased)
            .Build();

        // Assert
        Assert.Equal("SkillBased", parameters.Get<string>("delegationType"));
    }

    [Fact]
    public void ShouldAddCustomParameter_WhenUsingBuilderAdd()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .Add("custom", "value")
            .Add("number", 42)
            .Build();

        // Assert
        Assert.Equal("value", parameters.Get<string>("custom"));
        Assert.Equal(42, parameters.Get<int>("number"));
    }

    [Fact]
    public void ShouldAddMultipleParameters_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var timeout = TimeoutLong;

        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddMinSkillMatch(0.85)
            .AddMaxWorkload(7)
            .AddManagerRole("SeniorManager")
            .AddPriority(0.95)
            .AddTimeout(timeout)
            .AddRetryLimit(5)
            .AddDelegationType(DelegationType.WorkloadBased)
            .Add("customFlag", true)
            .Build();

        // Assert
        Assert.Equal(0.85, parameters.Get<double>("minSkillMatch"));
        Assert.Equal(7, parameters.Get<int>("maxWorkload"));
        Assert.Equal("SeniorManager", parameters.Get<string>("managerRole"));
        Assert.Equal(0.95, parameters.Get<double>("priority"));
        Assert.Equal(timeout, parameters.Get<TimeSpan>("timeout"));
        Assert.Equal(5, parameters.Get<int>("retryLimit"));
        Assert.Equal("WorkloadBased", parameters.Get<string>("delegationType"));
        Assert.True(parameters.Get<bool>("customFlag"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var parameters = DelegationParameters.CreateBuilder()
            .AddMinSkillMatch(0.7)
            .AddMinSkillMatch(0.9)
            .Build();

        // Assert
        Assert.Equal(0.9, parameters.Get<double>("minSkillMatch"));
    }

    [Fact]
    public void ShouldAcceptVariousTypes_WhenUsingDelegationParameterValueFrom()
    {
        // Act & Assert - Should not throw
        var stringValue = DelegationParameterValue.From("text");
        var intValue = DelegationParameterValue.From(123);
        var boolValue = DelegationParameterValue.From(true);
        var dateValue = DelegationParameterValue.From(DateTime.UtcNow);
        var timeSpanValue = DelegationParameterValue.From(TimeSpan.FromHours(1));
        var objectValue = DelegationParameterValue.From(new { Test = "value" });

        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(dateValue);
        Assert.NotNull(timeSpanValue);
        Assert.NotNull(objectValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingDelegationParameterValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => DelegationParameterValue.From(null!));
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingDelegationParameterValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = DelegationParameterValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingDelegationParameterValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = DelegationParameterValue.From(123);

        // Act
        var asString = value.GetValue<string>();
        var asDouble = value.GetValue<double>();

        // Assert
        Assert.Equal("123", asString);
        Assert.Equal(123.0, asDouble);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingDelegationParameterValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = DelegationParameterValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert delegation parameter value", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingDelegationParameterValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = DelegationParameterValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Equal(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingDelegationParameterValueUsingValueType()
    {
        // Arrange
        var stringValue = DelegationParameterValue.From("test");
        var intValue = DelegationParameterValue.From(123);
        var boolValue = DelegationParameterValue.From(true);

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(bool), boolValue.ValueType);
    }

    [Fact]
    public void ShouldCreateImmutableParameters_WhenUsingDelegationParametersUsingConstructor()
    {
        // Arrange
        var parameters = DelegationParameters.CreateBuilder()
            .Add("key", "value")
            .Build();

        // Try to modify through builder (should not affect built instance)
        var builder = DelegationParameters.CreateBuilder();
        builder.Add("key", "modified");

        // Act & Assert - Original should be unchanged
        Assert.Equal("value", parameters.Get<string>("key"));
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingComplexScenarioUsingCombiningMultipleBuilders()
    {
        // Arrange
        // Create base parameters for skill-based delegation
        var baseParams = DelegationParameters.ForSkillBased(0.9);

        // Create additional parameters
        var additionalParams = DelegationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.FromMinutes(20))
            .AddRetryLimit(3)
            .AddPriority(0.8)
            .Build();

        // Act - In real usage, you might combine these in application logic
        // For testing, we verify they work independently

        // Assert
        Assert.Equal(0.9, baseParams.Get<double>("minSkillMatch"));
        Assert.Equal("SkillBased", baseParams.Get<string>("delegationType"));

        Assert.Equal(TimeSpan.FromMinutes(20), additionalParams.Get<TimeSpan>("timeout"));
        Assert.Equal(3, additionalParams.Get<int>("retryLimit"));
        Assert.Equal(0.8, additionalParams.Get<double>("priority"));
    }
}
