using Orkeon.Infrastructure.LLMs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class LlmMetadataTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenLlmResponseMetadataEmpty()
    {
        // Act
        var metadata = LlmResponseMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.ToDictionary());
    }

    [Fact]
    public void ShouldReturnDefault_WhenLlmResponseMetadataGetWithNonExistentKey()
    {
        // Arrange
        var metadata = LlmResponseMetadata.Empty;

        // Act
        var result = metadata.Get<string>("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldAddProviderMetadata_WhenLlmResponseMetadataBuilderAddProvider()
    {
        // Arrange & Act
        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddProvider("OpenAI")
            .Build();

        // Assert
        var dict = metadata.ToDictionary();
        Assert.Single(dict);
        Assert.Equal("OpenAI", dict["provider"]);
    }

    [Fact]
    public void ShouldAddErrorMetadata_WhenLlmResponseMetadataBuilderAddError()
    {
        // Arrange & Act
        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddError("Connection timeout")
            .AddErrorType("TimeoutException")
            .Build();

        // Assert
        var dict = metadata.ToDictionary();
        Assert.Equal(2, dict.Count);
        Assert.Equal("Connection timeout", dict["error"]);
        Assert.Equal("TimeoutException", dict["error_type"]);
    }

    [Fact]
    public void ShouldAddDurationMetadata_WhenLlmResponseMetadataBuilderAddDuration()
    {
        // Arrange & Act
        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddTotalDuration(1500)
            .AddEvalDuration(500)
            .Build();

        // Assert
        var dict = metadata.ToDictionary();
        Assert.Equal(2, dict.Count);
        Assert.Equal(1500L, dict["total_duration"]);
        Assert.Equal(500L, dict["eval_duration"]);
    }

    [Fact]
    public void ShouldAddDoneMetadata_WhenLlmResponseMetadataBuilderAddDone()
    {
        // Arrange & Act
        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddDone(true)
            .Build();

        // Assert
        var dict = metadata.ToDictionary();
        Assert.Single(dict);
        Assert.True((bool)dict["done"]);
    }

    [Fact]
    public void ShouldAddCustomMetadata_WhenLlmResponseMetadataBuilderAdd()
    {
        // Arrange & Act
        var metadata = LlmResponseMetadata.CreateBuilder()
            .Add("custom_key", "custom_value")
            .Add("tokens", 150)
            .Build();

        // Assert
        var dict = metadata.ToDictionary();
        Assert.Equal(2, dict.Count);
        Assert.Equal("custom_value", dict["custom_key"]);
        Assert.Equal(150, dict["tokens"]);
    }

    [Fact]
    public void ShouldReturnTypedValue_WhenLlmResponseMetadataGetWithExistingKey()
    {
        // Arrange
        var metadata = LlmResponseMetadata.CreateBuilder()
            .Add("string_value", "test")
            .Add("int_value", 42)
            .Add("bool_value", true)
            .Build();

        // Act
        var stringVal = metadata.Get<string>("string_value");
        var intVal = metadata.Get<int>("int_value");
        var boolVal = metadata.Get<bool>("bool_value");

        // Assert
        Assert.Equal("test", stringVal);
        Assert.Equal(42, intVal);
        Assert.True(boolVal);
    }

    [Fact]
    public void ShouldReturnEmptyInstance_WhenOllamaRequestOptionsEmpty()
    {
        // Act
        var options = OllamaRequestOptions.Empty;

        // Assert
        Assert.NotNull(options);
        Assert.Empty(options.ToDictionary());
    }

    [Fact]
    public void ShouldAddTemperatureOption_WhenOllamaRequestOptionsBuilderAddTemperature()
    {
        // Arrange & Act
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(0.7)
            .Build();

        // Assert
        var dict = options.ToDictionary();
        Assert.Single(dict);
        Assert.Equal(0.7, dict["temperature"]);
    }

    [Fact]
    public void ShouldAddAllOptions_WhenOllamaRequestOptionsBuilderAddAllOptions()
    {
        // Arrange & Act
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(0.8)
            .AddNumPredict(100)
            .AddTopK(40)
            .AddTopP(0.9)
            .AddSeed(42)
            .Build();

        // Assert
        var dict = options.ToDictionary();
        Assert.Equal(5, dict.Count);
        Assert.Equal(0.8, dict["temperature"]);
        Assert.Equal(100, dict["num_predict"]);
        Assert.Equal(40, dict["top_k"]);
        Assert.Equal(0.9, dict["top_p"]);
        Assert.Equal(42, dict["seed"]);
    }

    [Fact]
    public void ShouldAddCustomOption_WhenOllamaRequestOptionsBuilderAdd()
    {
        // Arrange & Act
        var options = OllamaRequestOptions.CreateBuilder()
            .Add("custom_option", "value")
            .Build();

        // Assert
        var dict = options.ToDictionary();
        Assert.Single(dict);
        Assert.Equal("value", dict["custom_option"]);
    }

    [Fact]
    public void ShouldAddModelToPayload_WhenOllamaRequestPayloadBuilderAddModel()
    {
        // Arrange & Act
        var payload = OllamaRequestPayload.CreateBuilder()
            .AddModel(ModelLlama2)
            .Build();

        // Assert
        var dict = payload.ToDictionary();
        Assert.Single(dict);
        Assert.Equal(ModelLlama2, dict["model"]);
    }

    [Fact]
    public void ShouldAddPromptToPayload_WhenOllamaRequestPayloadBuilderAddPrompt()
    {
        // Arrange & Act
        var payload = OllamaRequestPayload.CreateBuilder()
            .AddPrompt("Hello, world!")
            .Build();

        // Assert
        var dict = payload.ToDictionary();
        Assert.Single(dict);
        Assert.Equal("Hello, world!", dict["prompt"]);
    }

    [Fact]
    public void ShouldAddStreamToPayload_WhenOllamaRequestPayloadBuilderAddStream()
    {
        // Arrange & Act
        var payload = OllamaRequestPayload.CreateBuilder()
            .AddStream(true)
            .Build();

        // Assert
        var dict = payload.ToDictionary();
        Assert.Single(dict);
        Assert.True((bool)dict["stream"]);
    }

    [Fact]
    public void ShouldAddOptionsToPayload_WhenOllamaRequestPayloadBuilderAddOptions()
    {
        // Arrange
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(0.5)
            .Build();

        // Act
        var payload = OllamaRequestPayload.CreateBuilder()
            .AddOptions(options)
            .Build();

        // Assert
        var dict = payload.ToDictionary();
        Assert.Single(dict);
        Assert.NotNull(dict["options"]);
        var optionsDict = dict["options"] as Dictionary<string, object>;
        Assert.NotNull(optionsDict);
        Assert.Equal(0.5, optionsDict["temperature"]);
    }

    [Fact]
    public void ShouldContainAllFields_WhenOllamaRequestPayloadBuilderCompletePayload()
    {
        // Arrange
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(0.7)
            .AddNumPredict(50)
            .Build();

        // Act
        var payload = OllamaRequestPayload.CreateBuilder()
            .AddModel("codellama")
            .AddPrompt("Write a function")
            .AddStream(false)
            .AddOptions(options)
            .Build();

        // Assert
        var dict = payload.ToDictionary();
        Assert.Equal(4, dict.Count);
        Assert.Equal("codellama", dict["model"]);
        Assert.Equal("Write a function", dict["prompt"]);
        Assert.False((bool)dict["stream"]);
        Assert.NotNull(dict["options"]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenLlmMetadataValueFromWithNullValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => LlmMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateInstance_WhenLlmMetadataValueFromWithValidValue()
    {
        // Act
        var value = LlmMetadataValue.From("test");

        // Assert
        Assert.NotNull(value);
        Assert.Equal("test", value.RawValue);
        Assert.Equal(typeof(string), value.ValueType);
    }

    [Fact]
    public void ShouldReturnValue_WhenLlmMetadataValueGetValueWithCorrectType()
    {
        // Arrange
        var value = LlmMetadataValue.From(42);

        // Act
        var result = value.GetValue<int>();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldConvertValue_WhenLlmMetadataValueGetValueWithConvertibleType()
    {
        // Arrange
        var value = LlmMetadataValue.From(42);

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenLlmMetadataValueGetValueWithIncompatibleType()
    {
        // Arrange
        var value = LlmMetadataValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert LLM metadata value", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenLlmMetadataValueRawValue()
    {
        // Arrange
        var originalValue = new DateTime(2024, 1, 1);
        var value = LlmMetadataValue.From(originalValue);

        // Act
        var rawValue = value.RawValue;

        // Assert
        Assert.Equal(originalValue, rawValue);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenLlmMetadataValueValueType()
    {
        // Arrange
        var value = LlmMetadataValue.From(3.14);

        // Act
        var type = value.ValueType;

        // Assert
        Assert.Equal(typeof(double), type);
    }

    [Fact]
    public void ShouldBuildCorrectMetadata_WhenLlmResponseMetadataBuilderChainedCalls()
    {
        // Act
        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddProvider("Ollama")
            .AddDone(true)
            .AddTotalDuration(2000)
            .AddEvalDuration(800)
            .Add("model", "llama2:7b")
            .Add("tokens_generated", 150)
            .Build();

        // Assert
        var dict = metadata.ToDictionary();
        Assert.Equal(6, dict.Count);
        Assert.Equal("Ollama", dict["provider"]);
        Assert.True((bool)dict["done"]);
        Assert.Equal(2000L, dict["total_duration"]);
        Assert.Equal(800L, dict["eval_duration"]);
        Assert.Equal("llama2:7b", dict["model"]);
        Assert.Equal(150, dict["tokens_generated"]);
    }

    [Fact]
    public void ShouldUseLastValue_WhenOllamaRequestOptionsBuilderOverwriteValue()
    {
        // Act
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(0.5)
            .AddTemperature(0.8)  // Overwrite
            .Build();

        // Assert
        var dict = options.ToDictionary();
        Assert.Single(dict);
        Assert.Equal(0.8, dict["temperature"]);
    }

    [Fact]
    public void ShouldWork_WhenLlmMetadataValueGetValueWithNullableType()
    {
        // Arrange
        var value = LlmMetadataValue.From(42);

        // Act
        var result = value.GetValue<int?>();

        // Assert
        Assert.Equal(42, result);
    }
}
