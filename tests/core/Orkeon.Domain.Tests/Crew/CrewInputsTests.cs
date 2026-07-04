using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// Tests for CrewInputs implementation.
/// Verifies typed input access, setting, and querying.
/// </summary>
public class CrewInputsTests
{
    #region Factory Methods

    [Fact]
    public void ShouldCreateEmptyInputs_WhenUsingEmptyFactory()
    {
        // Act
        var inputs = CrewInputs.Empty();

        // Assert
        Assert.NotNull(inputs);
        Assert.Empty(inputs.Data);
        Assert.NotNull(inputs.InputData);
    }

    [Fact]
    public void ShouldCreateFromDictionary_WhenUsingFromFactory()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        };

        // Act
        var inputs = CrewInputs.From(data);

        // Assert
        Assert.NotNull(inputs);
        Assert.Equal(2, inputs.Data.Count);
    }

    [Fact]
    public void ShouldCreateFromNullDictionary_WhenUsingFromFactory()
    {
        // Act
        var inputs = CrewInputs.From((Dictionary<string, object>?)null);

        // Assert
        Assert.NotNull(inputs);
        Assert.Empty(inputs.Data);
    }

    [Fact]
    public void ShouldCreateFromCrewInputData_WhenUsingFromFactory()
    {
        // Arrange
        var inputData = CrewInputData.CreateBuilder()
            .AddPrompt(TestPrompt)
            .AddGoal("Test goal")
            .Build();

        // Act
        var inputs = CrewInputs.From(inputData);

        // Assert
        Assert.NotNull(inputs);
        Assert.Equal(inputData, inputs.InputData);
    }

    #endregion

    #region GetValue

    [Fact]
    public void ShouldReturnTypedValue_WhenKeyExists()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["name"] = "test",
            ["count"] = 42
        };
        var inputs = CrewInputs.From(data);

        // Act
        var name = inputs.GetValue<string>("name");
        var count = inputs.GetValue<int>("count");

        // Assert
        Assert.Equal("test", name);
        Assert.Equal(42, count);
    }

    [Fact]
    public void ShouldReturnDefault_WhenKeyDoesNotExist()
    {
        // Arrange
        var inputs = CrewInputs.Empty();

        // Act
        var result = inputs.GetValue<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region HasValue

    [Fact]
    public void ShouldReturnTrue_WhenKeyExists()
    {
        // Arrange
        var data = new Dictionary<string, object> { ["key"] = "value" };
        var inputs = CrewInputs.From(data);

        // Act & Assert
        Assert.True(inputs.HasValue("key"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenKeyDoesNotExist()
    {
        // Arrange
        var inputs = CrewInputs.Empty();

        // Act & Assert
        Assert.False(inputs.HasValue("missing"));
    }

    #endregion

    #region SetValue

    [Fact]
    public void ShouldSetNewValue_WhenSettingOnEmptyInputs()
    {
        // Arrange
        var inputs = CrewInputs.Empty();

        // Act
        inputs.SetValue("key", "value");

        // Assert
        Assert.True(inputs.HasValue("key"));
        Assert.Equal("value", inputs.GetValue<string>("key"));
    }

    [Fact]
    public void ShouldOverwriteValue_WhenSettingExistingKey()
    {
        // Arrange
        var data = new Dictionary<string, object> { ["key"] = "old" };
        var inputs = CrewInputs.From(data);

        // Act
        inputs.SetValue("key", "new");

        // Assert
        Assert.Equal("new", inputs.GetValue<string>("key"));
    }

    [Fact]
    public void ShouldSetDifferentValueTypes_WhenSettingVariousTypes()
    {
        // Arrange
        var inputs = CrewInputs.Empty();

        // Act
        inputs.SetValue("str", "hello");
        inputs.SetValue("num", 123);
        inputs.SetValue("flag", true);

        // Assert
        Assert.Equal("hello", inputs.GetValue<string>("str"));
        Assert.Equal(123, inputs.GetValue<int>("num"));
        Assert.True(inputs.GetValue<bool>("flag"));
    }

    #endregion

    #region Data Property

    [Fact]
    public void ShouldReturnDictionary_WhenAccessingDataProperty()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            ["a"] = "alpha",
            ["b"] = "beta"
        };
        var inputs = CrewInputs.From(data);

        // Act
        var result = inputs.Data;

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("alpha", result["a"]);
        Assert.Equal("beta", result["b"]);
    }

    #endregion

    #region InputData Property

    [Fact]
    public void ShouldReturnCrewInputData_WhenAccessingInputDataProperty()
    {
        // Arrange
        var inputData = CrewInputData.CreateBuilder()
            .AddPrompt("test prompt")
            .Build();
        var inputs = CrewInputs.From(inputData);

        // Act
        var result = inputs.InputData;

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test prompt", result.GetValue<string>("prompt"));
    }

    #endregion
}
