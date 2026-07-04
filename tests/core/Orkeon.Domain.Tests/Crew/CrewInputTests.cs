using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Domain.Tests.Crew;

public class CrewInputTests
{
    [Fact]
    public void ShouldInitializeInput_WhenConstructingWithValidParameters()
    {
        // Arrange
        var initialContext = "Test context for crew execution";
        var parameters = new Dictionary<string, object>
        {
            { "maxIterations", 10 },
            { "timeout", 300 },
            { "enableDebug", true }
        };
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("test-run")
            .AddTag("unit-test")
            .Build();

        // Act
        var input = new CrewInput(initialContext, parameters, metadata);

        // Assert
        Assert.NotNull(input);
        Assert.Equal(initialContext, input.InitialContext);
        Assert.NotNull(input.Variables);
        Assert.NotNull(input.Parameters);
        Assert.Equal(3, input.Parameters.Count);
        Assert.Equal(metadata, input.Metadata);
        Assert.True(input.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var initialContext = "Minimal context";

        // Act
        var input = new CrewInput(initialContext);

        // Assert
        Assert.Equal(initialContext, input.InitialContext);
        Assert.NotNull(input.Variables);
        Assert.NotNull(input.Parameters);
        Assert.Empty(input.Parameters);
        Assert.Equal(CrewMetadata.Empty, input.Metadata);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithNullContext()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new CrewInput(null!));
        Assert.Equal("initialContext", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithEmptyContext()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new CrewInput(""));
        Assert.Equal("initialContext", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithWhitespaceContext()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new CrewInput("   "));
        Assert.Equal("initialContext", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingParameterWithExistingIntParameter()
    {
        // Arrange
        var parameters = new Dictionary<string, object> { { "count", 42 } };
        var input = new CrewInput("context", parameters);

        // Act
        var result = input.GetParameter<int>("count");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingParameterWithExistingBoolParameter()
    {
        // Arrange
        var parameters = new Dictionary<string, object> { { "enabled", true } };
        var input = new CrewInput("context", parameters);

        // Act
        var result = input.GetParameter<bool>("enabled");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingParameterWithExistingDoubleParameter()
    {
        // Arrange
        var parameters = new Dictionary<string, object> { { "rate", 3.14 } };
        var input = new CrewInput("context", parameters);

        // Act
        var result = input.GetParameter<double>("rate");

        // Assert
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingParameterWithNonExistentParameter()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act
        var result = input.GetParameter<int>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingStringParameterWithExistingParameter()
    {
        // Arrange
        var parameters = new Dictionary<string, object> { { "name", "test-crew" } };
        var input = new CrewInput("context", parameters);

        // Act
        var result = input.GetStringParameter("name");

        // Assert
        Assert.Equal("test-crew", result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingStringParameterWithNonExistentParameter()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act
        var result = input.GetStringParameter("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldAddParameter_WhenSettingParameterWithStringValue()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act
        var updated = input.WithParameter("name", "test-value");

        // Assert
        var result = updated.GetStringParameter("name");
        Assert.Equal("test-value", result);
    }

    [Fact]
    public void ShouldAddParameter_WhenSettingParameterWithIntValue()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act
        var updated = input.WithParameter("count", 100);

        // Assert
        var result = updated.GetParameter<int>("count");
        Assert.Equal(100, result);
    }

    [Fact]
    public void ShouldAddParameter_WhenSettingParameterWithBoolValue()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act
        var updated = input.WithParameter("enabled", false);

        // Assert
        var result = updated.GetParameter<bool>("enabled");
        Assert.False(result);
    }

    [Fact]
    public void ShouldAddParameter_WhenSettingParameterWithDoubleValue()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act
        var updated = input.WithParameter("rate", 2.5);

        // Assert
        var result = updated.GetParameter<double>("rate");
        Assert.Equal(2.5, result);
    }

    [Fact]
    public void ShouldConvertToString_WhenSettingParameterWithCustomObject()
    {
        // Arrange
        var input = new CrewInput("context");
        var customObject = new { Name = "Test", Value = 123 };

        // Act
        var updated = input.WithParameter("custom", customObject);

        // Assert
        var result = updated.GetStringParameter("custom");
        Assert.NotNull(result);
        Assert.Contains("Name", result);
        Assert.Contains("Test", result);
    }

    [Fact]
    public void ShouldUpdateValue_WhenSettingParameterWithExistingKey()
    {
        // Arrange
        var parameters = new Dictionary<string, object> { { "key", "initial" } };
        var input = new CrewInput("context", parameters);

        // Act
        var updated = input.WithParameter("key", "updated");

        // Assert
        var result = updated.GetStringParameter("key");
        Assert.Equal("updated", result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenSettingParameterWithNullKey()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => input.WithParameter(null!, "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenSettingParameterWithEmptyKey()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => input.WithParameter("", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenSettingParameterWithWhitespaceKey()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => input.WithParameter("   ", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSettingParameterWithNullValue()
    {
        // Arrange
        var input = new CrewInput("context");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => input.WithParameter("key", null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnDictionaryFromVariables_WhenUsingParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", true }
        };
        var input = new CrewInput("context", parameters);

        // Act
        var result = input.Parameters;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal(42, result["key2"]);
        Assert.True((bool)result["key3"]);
    }

    [Fact]
    public void ShouldBeSetToCurrentTime_WhenUsingCreatedAt()
    {
        // Arrange
        var timeBefore = DateTime.UtcNow;

        // Act
        var input = new CrewInput("context");
        var timeAfter = DateTime.UtcNow;

        // Assert
        Assert.True(input.CreatedAt >= timeBefore);
        Assert.True(input.CreatedAt <= timeAfter);
    }

    [Fact]
    public void ShouldUseEmpty_WhenUsingMetadataWithNullMetadata()
    {
        // Arrange & Act
        var input = new CrewInput("context", null, null);

        // Assert
        Assert.Equal(CrewMetadata.Empty, input.Metadata);
    }

    [Fact]
    public void ShouldUseIt_WhenConstructingWithProvidedMetadata()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("test-id")
            .AddTag("test-type")
            .Build();

        // Act
        var input = new CrewInput("context", null, metadata);

        // Assert
        Assert.Equal(metadata, input.Metadata);
    }
}
