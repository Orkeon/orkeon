using Orkeon.Domain.Flows.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class FlowConfigurationSettingsTests
{
    #region FlowConfigurationSettings Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var settings = FlowConfigurationSettings.Empty;

        // Assert
        Assert.NotNull(settings);
        Assert.Empty(settings.Keys);
        Assert.Equal(0, settings.Count);
        Assert.False(settings.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddRetryPolicy("exponential")
            .AddCacheEnabled(true)
            .Build();

        // Act & Assert
        Assert.Equal(4, settings.Get<int>("parallelism"));
        Assert.Equal("exponential", settings.Get<string>("retry_policy"));
        Assert.True(settings.Get<bool>("cache_enabled"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var settings = FlowConfigurationSettings.Empty;

        // Act & Assert
        Assert.Null(settings.Get<string>("missing"));
        Assert.Equal(0, settings.Get<int>("missing"));
        Assert.False(settings.Get<bool>("missing"));
        Assert.Equal(0.0, settings.Get<double>("missing"));
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddStateStore("redis")
            .Build();

        // Act & Assert
        Assert.True(settings.ContainsKey("state_store"));
        Assert.False(settings.ContainsKey("missing_key"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(2)
            .AddRetryPolicy("linear")
            .AddErrorHandling("continue")
            .Build();

        // Act
        var keys = settings.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("parallelism", keys);
        Assert.Contains("retry_policy", keys);
        Assert.Contains("error_handling", keys);
    }

    [Fact]
    public void ShouldReturnNumberOfSettings_WhenCounting()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddCacheEnabled(true)
            .AddStateStore("memory")
            .AddErrorHandling("stop")
            .Build();

        // Act & Assert
        Assert.Equal(4, settings.Count);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedValue_WhenSetting()
    {
        // Arrange
        var original = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(2)
            .Build();

        // Act
        var updated = original.Set("parallelism", 4);
        var added = updated.Set("new_setting", "value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.NotSame(updated, added);

        Assert.Equal(2, original.Get<int>("parallelism"));
        Assert.Equal(4, updated.Get<int>("parallelism"));
        Assert.Equal("value", added.Get<string>("new_setting"));

        Assert.Equal(1, original.Count);
        Assert.Equal(1, updated.Count);
        Assert.Equal(2, added.Count);
    }

    [Fact]
    public void ShouldReturnAllSettings_WhenUsingToDictionary()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(8)
            .AddRetryPolicy("fixed")
            .AddCacheEnabled(false)
            .AddStateStore("sqlite")
            .AddErrorHandling("retry")
            .Build();

        // Act
        var dict = settings.ToDictionary();

        // Assert
        Assert.Equal(5, dict.Count);
        Assert.Equal(8, dict["parallelism"]);
        Assert.Equal("fixed", dict["retry_policy"]);
        Assert.False((bool)dict["cache_enabled"]);
        Assert.Equal("sqlite", dict["state_store"]);
        Assert.Equal("retry", dict["error_handling"]);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddParallelism()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(16)
            .Build();

        // Assert
        Assert.Equal(16, settings.Get<int>("parallelism"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddRetryPolicy()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddRetryPolicy("exponential_backoff")
            .Build();

        // Assert
        Assert.Equal("exponential_backoff", settings.Get<string>("retry_policy"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddErrorHandling()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddErrorHandling("ignore_and_continue")
            .Build();

        // Assert
        Assert.Equal("ignore_and_continue", settings.Get<string>("error_handling"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddCacheEnabled()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddCacheEnabled(true)
            .Build();

        // Assert
        Assert.True(settings.Get<bool>("cache_enabled"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddStateStore()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddStateStore("distributed_cache")
            .Build();

        // Assert
        Assert.Equal("distributed_cache", settings.Get<string>("state_store"));
    }

    [Fact]
    public void ShouldAddCustomSettings_WhenUsingBuilderAdd()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .Add("custom_string", "value")
            .Add("custom_int", 42)
            .Add("custom_bool", true)
            .Add("custom_double", 3.14)
            .Add("custom_datetime", DateTime.UtcNow)
            .Build();

        // Assert
        Assert.Equal("value", settings.Get<string>("custom_string"));
        Assert.Equal(42, settings.Get<int>("custom_int"));
        Assert.True(settings.Get<bool>("custom_bool"));
        Assert.Equal(3.14, settings.Get<double>("custom_double"));
        // DateTime is a value type, it's never null - removed Assert.NotNull
    }

    [Fact]
    public void ShouldAddAllSettings_WhenUsingBuilderChainedCalls()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddRetryPolicy("linear")
            .AddErrorHandling("stop_on_error")
            .AddCacheEnabled(true)
            .AddStateStore("in_memory")
            .Add("custom_timeout", 300)
            .Build();

        // Assert
        Assert.Equal(6, settings.Count);
        Assert.Equal(4, settings.Get<int>("parallelism"));
        Assert.Equal("linear", settings.Get<string>("retry_policy"));
        Assert.Equal("stop_on_error", settings.Get<string>("error_handling"));
        Assert.True(settings.Get<bool>("cache_enabled"));
        Assert.Equal("in_memory", settings.Get<string>("state_store"));
        Assert.Equal(300, settings.Get<int>("custom_timeout"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(2)
            .AddParallelism(8)
            .Build();

        // Assert
        Assert.Equal(8, settings.Get<int>("parallelism"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var settings = FlowConfigurationSettings.FromDictionary(null);

        // Assert
        Assert.Same(FlowConfigurationSettings.Empty, settings);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var settings = FlowConfigurationSettings.FromDictionary([]);

        // Assert
        Assert.Same(FlowConfigurationSettings.Empty, settings);
    }

    [Fact]
    public void ShouldCreateSettings_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "parallelism", 6 },
            { "retry_policy", "constant" },
            { "cache_enabled", false },
            { "state_store", "mongodb" },
            { "timeout_seconds", 120 }
        };

        // Act
        var settings = FlowConfigurationSettings.FromDictionary(dict);

        // Assert
        Assert.Equal(5, settings.Count);
        Assert.Equal(6, settings.Get<int>("parallelism"));
        Assert.Equal("constant", settings.Get<string>("retry_policy"));
        Assert.False(settings.Get<bool>("cache_enabled"));
        Assert.Equal("mongodb", settings.Get<string>("state_store"));
        Assert.Equal(120, settings.Get<int>("timeout_seconds"));
    }

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripToDictionaryFromDictionary()
    {
        // Arrange
        var original = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(3)
            .AddRetryPolicy("adaptive")
            .AddCacheEnabled(true)
            .AddStateStore("cassandra")
            .Add("custom_value", 99.9)
            .Build();

        // Act
        var dict = original.ToDictionary();
        var restored = FlowConfigurationSettings.FromDictionary(dict);

        // Assert
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal(3, restored.Get<int>("parallelism"));
        Assert.Equal("adaptive", restored.Get<string>("retry_policy"));
        Assert.True(restored.Get<bool>("cache_enabled"));
        Assert.Equal("cassandra", restored.Get<string>("state_store"));
        Assert.Equal(99.9, restored.Get<double>("custom_value"));
    }

    #endregion

    #region FlowSettingValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingFlowSettingValueFromWithValidValue()
    {
        // Act
        var stringValue = FlowSettingValue.From("test");
        var intValue = FlowSettingValue.From(42);
        var boolValue = FlowSettingValue.From(true);
        var doubleValue = FlowSettingValue.From(3.14);
        var dateValue = FlowSettingValue.From(DateTime.UtcNow);

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(doubleValue);
        Assert.NotNull(dateValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowSettingValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => FlowSettingValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingFlowSettingValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = FlowSettingValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingFlowSettingValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = FlowSettingValue.From(123);

        // Act
        var asString = intValue.GetValue<string>();
        var asDouble = intValue.GetValue<double>();
        var asLong = intValue.GetValue<long>();

        // Assert
        Assert.Equal("123", asString);
        Assert.Equal(123.0, asDouble);
        Assert.Equal(123L, asLong);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingFlowSettingValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = FlowSettingValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert flow setting value", exception.Message);
        Assert.Contains("String to Int32", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingFlowSettingValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = FlowSettingValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingFlowSettingValueUsingValueType()
    {
        // Arrange
        var stringValue = FlowSettingValue.From("test");
        var intValue = FlowSettingValue.From(42);
        var boolValue = FlowSettingValue.From(true);
        var listValue = FlowSettingValue.From(new List<string> { "a", "b" });

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(bool), boolValue.ValueType);
        Assert.Equal(typeof(List<string>), listValue.ValueType);
    }

    #endregion

    #region FlowConfigurationSettings Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoSettingsWithSameValues()
    {
        // Arrange
        var settings1 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddRetryPolicy("exponential")
            .AddCacheEnabled(true)
            .Build();

        var settings2 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddRetryPolicy("exponential")
            .AddCacheEnabled(true)
            .Build();

        // Act & Assert
        Assert.Equal(settings1, settings2);
        Assert.True(settings1.Equals(settings2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoSettingsWithDifferentValues()
    {
        // Arrange
        var settings1 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .Build();

        var settings2 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(8)
            .Build();

        // Act & Assert
        Assert.NotEqual(settings1, settings2);
        Assert.False(settings1.Equals(settings2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingSettingsWithDifferentCounts()
    {
        // Arrange
        var settings1 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .Build();

        var settings2 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddCacheEnabled(true)
            .Build();

        // Act & Assert
        Assert.NotEqual(settings1, settings2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingSettingsWithNull()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .Build();

        // Act & Assert
        Assert.False(settings.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingSettingsWithSameReference()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .Build();

        // Act & Assert
        Assert.True(settings.Equals(settings));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingSettingsWithDifferentObjectType()
    {
        // Arrange
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .Build();

        // Act & Assert
        Assert.False(settings.Equals("not FlowConfigurationSettings"));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoSettingsAreEqual()
    {
        // Arrange
        var settings1 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddCacheEnabled(false)
            .Build();

        var settings2 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddCacheEnabled(false)
            .Build();

        // Act & Assert
        Assert.Equal(settings1.GetHashCode(), settings2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoSettingsAreDifferent()
    {
        // Arrange
        var settings1 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .Build();

        var settings2 = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(8)
            .Build();

        // Act & Assert
        Assert.NotEqual(settings1.GetHashCode(), settings2.GetHashCode());
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoEmptySettings()
    {
        // Arrange
        var settings1 = FlowConfigurationSettings.Empty;
        var settings2 = FlowConfigurationSettings.Empty;

        // Act & Assert
        Assert.Equal(settings1, settings2);
        Assert.Equal(settings1.GetHashCode(), settings2.GetHashCode());
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFlowSettingsWithMultipleTypes_WhenUsingComplexScenario()
    {
        // Arrange & Act
        var settings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(10)
            .AddRetryPolicy("exponential")
            .AddCacheEnabled(true)
            .AddStateStore("redis")
            .AddErrorHandling("circuit_breaker")
            .Add("max_retries", 5)
            .Add("retry_delay_ms", 1000)
            .Add("circuit_breaker_threshold", 0.5)
            .Add("cache_ttl_seconds", 3600)
            .Add("state_sync_interval", TimeoutStandard)
            .Build();

        // Assert
        Assert.Equal(10, settings.Count);
        Assert.Equal(10, settings.Get<int>("parallelism"));
        Assert.Equal("exponential", settings.Get<string>("retry_policy"));
        Assert.True(settings.Get<bool>("cache_enabled"));
        Assert.Equal(5, settings.Get<int>("max_retries"));
        Assert.Equal(1000, settings.Get<int>("retry_delay_ms"));
        Assert.Equal(0.5, settings.Get<double>("circuit_breaker_threshold"));
        Assert.Equal(3600, settings.Get<int>("cache_ttl_seconds"));
        Assert.Equal(TimeoutStandard, settings.Get<TimeSpan>("state_sync_interval"));
    }

    [Fact]
    public void ShouldImmutabilityCheck_WhenUsingComplexScenario()
    {
        // Arrange
        var original = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(4)
            .AddCacheEnabled(true)
            .Build();

        // Act - Multiple modifications
        var modified1 = original.Set("parallelism", 8);
        var modified2 = modified1.Set("cache_enabled", false);
        var modified3 = modified2.Set("new_setting", "value");

        // Assert - Each instance is independent
        Assert.Equal(2, original.Count);
        Assert.Equal(4, original.Get<int>("parallelism"));
        Assert.True(original.Get<bool>("cache_enabled"));

        Assert.Equal(2, modified1.Count);
        Assert.Equal(8, modified1.Get<int>("parallelism"));
        Assert.True(modified1.Get<bool>("cache_enabled"));

        Assert.Equal(2, modified2.Count);
        Assert.Equal(8, modified2.Get<int>("parallelism"));
        Assert.False(modified2.Get<bool>("cache_enabled"));

        Assert.Equal(3, modified3.Count);
        Assert.Equal(8, modified3.Get<int>("parallelism"));
        Assert.False(modified3.Get<bool>("cache_enabled"));
        Assert.Equal("value", modified3.Get<string>("new_setting"));
    }

    [Fact]
    public void ShouldDifferentSettingProfiles_WhenUsingComplexScenario()
    {
        // Arrange
        var developmentSettings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(1)
            .AddRetryPolicy("none")
            .AddCacheEnabled(false)
            .AddStateStore("memory")
            .AddErrorHandling("log_and_continue")
            .Build();

        var productionSettings = FlowConfigurationSettings.CreateBuilder()
            .AddParallelism(16)
            .AddRetryPolicy("exponential_backoff")
            .AddCacheEnabled(true)
            .AddStateStore("redis_cluster")
            .AddErrorHandling("circuit_breaker")
            .Add("monitoring_enabled", true)
            .Add("alert_threshold", 100)
            .Build();

        // Act & Assert
        Assert.Equal(5, developmentSettings.Count);
        Assert.Equal(1, developmentSettings.Get<int>("parallelism"));
        Assert.False(developmentSettings.Get<bool>("cache_enabled"));

        Assert.Equal(7, productionSettings.Count);
        Assert.Equal(16, productionSettings.Get<int>("parallelism"));
        Assert.True(productionSettings.Get<bool>("cache_enabled"));
        Assert.True(productionSettings.Get<bool>("monitoring_enabled"));
        Assert.Equal(100, productionSettings.Get<int>("alert_threshold"));
    }

    #endregion
}
