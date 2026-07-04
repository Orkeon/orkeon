using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Application.Tests.DTOs.Common;

public class ToolMetadataTests
{
    // ══════════════ ToolDtoMetadata — Builder ══════════════

    [Fact]
    public void ShouldBuildMetadata_WhenUsingBuilder()
    {
        // Act
        var metadata = ToolDtoMetadata.CreateBuilder()
            .AddToolType(ToolFileRead)
            .AddToolName("file_read")
            .AddCategory("filesystem")
            .Add("custom", 99)
            .Build();

        // Assert
        Assert.Equal(ToolFileRead, metadata.Get<string>("tool_type"));
        Assert.Equal("file_read", metadata.Get<string>("tool_name"));
        Assert.Equal("filesystem", metadata.Get<string>("category"));
        Assert.Equal(99, metadata.Get<int>("custom"));
    }

    [Fact]
    public void ShouldReturnEmptyMetadata_WhenUsingStaticEmpty()
    {
        // Act
        var metadata = ToolDtoMetadata.Empty;
        var dict = metadata.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldConvertToDictionary_WhenCalled()
    {
        // Arrange
        var metadata = ToolDtoMetadata.CreateBuilder()
            .AddToolType(ToolHttpApi)
            .Build();

        // Act
        var dict = metadata.ToDictionary();

        // Assert
        Assert.Single(dict);
        Assert.Equal(ToolHttpApi, dict["tool_type"]);
    }

    [Fact]
    public void ShouldReturnDefault_WhenKeyNotFound()
    {
        // Arrange
        var metadata = ToolDtoMetadata.Empty;

        // Act
        var result = metadata.Get<string>("missing");

        // Assert
        Assert.Null(result);
    }

    // ══════════════ ToolConfigurationData — DefaultConfiguration ══════════════

    [Fact]
    public void ShouldCreateDefaultConfiguration_WhenCallingCreateDefault()
    {
        // Act
        var config = ToolConfigurationData.CreateDefault();

        // Assert
        Assert.Equal(30, config.Get<int>("timeout_seconds"));
        Assert.Equal(3, config.Get<int>("retry_attempts"));
        Assert.True(config.Get<bool>("cache_results"));
        Assert.True(config.Get<bool>("validate_input"));
        Assert.True(config.Get<bool>("sanitize_output"));
    }

    [Fact]
    public void ShouldReturnEmptyConfiguration_WhenUsingStaticEmpty()
    {
        // Act
        var config = ToolConfigurationData.Empty;
        var dict = config.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldBuildCustomConfiguration_WhenUsingBuilder()
    {
        // Act
        var config = ToolConfigurationData.CreateBuilder()
            .AddTimeoutSeconds(60)
            .AddRetryAttempts(5)
            .AddCacheResults(false)
            .AddValidateInput(true)
            .AddSanitizeOutput(false)
            .Add("custom_key", "custom_val")
            .Build();

        // Assert
        Assert.Equal(60, config.Get<int>("timeout_seconds"));
        Assert.Equal(5, config.Get<int>("retry_attempts"));
        Assert.False(config.Get<bool>("cache_results"));
        Assert.Equal("custom_val", config.Get<string>("custom_key"));
    }

    // ══════════════ ToolValidationRules ══════════════

    [Fact]
    public void ShouldBuildValidationRules_WhenUsingBuilder()
    {
        // Act
        var rules = ToolValidationRules.CreateBuilder()
            .AddRequiredFields(["path", "content"])
            .AddMaxLength(ParamPath, 260)
            .AddPattern(ParamPath, @"^[a-zA-Z]:\\.*$")
            .Build();

        // Assert
        var dict = rules.ToDictionary();
        Assert.Equal(3, dict.Count);
        Assert.Contains("required_fields", dict.Keys);
        Assert.Contains("path_max_length", dict.Keys);
        Assert.Contains("path_pattern", dict.Keys);
    }

    [Fact]
    public void ShouldReturnEmptyRules_WhenUsingStaticEmpty()
    {
        // Act
        var rules = ToolValidationRules.Empty;
        var dict = rules.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldAddCustomRule_WhenUsingGenericAdd()
    {
        // Act
        var rules = ToolValidationRules.CreateBuilder()
            .Add("min_value", 0)
            .Add("max_value", 100)
            .Build();

        // Assert
        var dict = rules.ToDictionary();
        Assert.Equal(2, dict.Count);
        Assert.Equal(0, dict["min_value"]);
        Assert.Equal(100, dict["max_value"]);
    }

    // ══════════════ ToolInputData ══════════════

    [Fact]
    public void ShouldBuildInputData_WhenUsingBuilder()
    {
        // Act
        var input = ToolInputData.CreateBuilder()
            .AddParsedFromMetadata(true)
            .AddQuery("SELECT * FROM users")
            .AddFilePath("/tmp/data.csv")
            .AddUrl(new Uri("https://api.example.com"))
            .AddInput("extra", "value")
            .Build();

        // Assert
        var dict = input.ToDictionary();
        Assert.Equal(5, dict.Count);
        Assert.Equal(true, dict["parsed_from_metadata"]);
        Assert.Equal("SELECT * FROM users", dict[ParamQuery]);
        Assert.Equal("/tmp/data.csv", dict["file_path"]);
        Assert.Equal(new Uri("https://api.example.com"), dict[ParamUrl]);
    }

    [Fact]
    public void ShouldCreateParsedFromMetadata_WhenCallingFactory()
    {
        // Act
        var input = ToolInputData.CreateParsedFromMetadata();
        var dict = input.ToDictionary();

        // Assert
        Assert.Single(dict);
        Assert.Equal(true, dict["parsed_from_metadata"]);
    }

    [Fact]
    public void ShouldReturnEmptyInputData_WhenUsingStaticEmpty()
    {
        // Act
        var dict = ToolInputData.Empty.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    // ══════════════ ToolMetadataValue — GetValue type conversion ══════════════

    [Fact]
    public void ShouldReturnDirectType_WhenValueMatchesRequestedType()
    {
        // Arrange
        var val = ToolMetadataValue.From(42);

        // Act
        var result = val.GetValue<int>();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldConvertType_WhenConvertChangeTypeSucceeds()
    {
        // Arrange
        var val = ToolMetadataValue.From(42);

        // Act
        var result = val.GetValue<double>();

        // Assert
        Assert.Equal(42.0, result);
    }

    [Fact]
    public void ShouldThrowInvalidCast_WhenConversionFails()
    {
        // Arrange
        var val = ToolMetadataValue.From(new List<string> { "a" });

        // Act & Assert
        Assert.Throws<InvalidCastException>(() => val.GetValue<int>());
    }

    [Fact]
    public void ShouldExposeRawValueAndType_WhenAccessing()
    {
        // Arrange
        var val = ToolMetadataValue.From("test");

        // Assert
        Assert.Equal("test", val.RawValue);
        Assert.Equal(typeof(string), val.ValueType);
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenCreatingFromNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ToolMetadataValue.From(null!));
    }
}
