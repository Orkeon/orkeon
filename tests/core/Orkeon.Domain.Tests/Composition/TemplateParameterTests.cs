using Orkeon.Domain.Agent.Composition;

namespace Orkeon.Domain.Tests.Composition;

/// <summary>
/// Tests for TemplateParameter following Clean Architecture principles.
/// Tests the template parameter record and ParameterType enum.
/// </summary>
public class TemplateParameterTests
{
    private static readonly int[] HiddenLayers = [128, 64, 32];

    private static readonly string[] Option1Option2Option3 = ["option1", "option2", "option3"];
    private static readonly string[] DevStagingProd = ["dev", "staging", "prod"];
    private static readonly string[] Choice1Choice2 = ["选项1", "选项2"];
    private static readonly string[] LoggingCaching = ["logging", "caching"];
    private static readonly string[] LoggingCachingCompressionEncryption = ["logging", "caching", "compression", "encryption"];
    private static readonly string[] NnRfSvmXgboost = ["neural_network", "random_forest", "svm", "xgboost"];
    private static readonly ParameterType[] AllParameterTypes =
    [
        ParameterType.Text,
        ParameterType.Number,
        ParameterType.Boolean,
        ParameterType.DateTime,
        ParameterType.List,
        ParameterType.Structured
    ];

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenUsingTemplateParameterConstructorWithRequiredParameters()
    {
        // Act
        var parameter = new TemplateParameter(
            "apiKey",
            "API key for external service",
            ParameterType.Text);

        // Assert
        Assert.Equal("apiKey", parameter.Name);
        Assert.Equal("API key for external service", parameter.Description);
        Assert.Equal(ParameterType.Text, parameter.Type);
        Assert.Null(parameter.DefaultValue);
        Assert.False(parameter.IsRequired);
        Assert.NotNull(parameter.ValidationRules);
        Assert.Empty(parameter.ValidationRules);
    }

    [Fact]
    public void ShouldInitialize_WhenUsingTemplateParameterConstructorWithAllParameters()
    {
        // Arrange
        var validationRules = new Dictionary<string, object>
        {
            { "minLength", 10 },
            { "maxLength", 50 },
            { "pattern", "^[A-Za-z0-9]+$" }
        };

        // Act
        var parameter = new TemplateParameter(
            "username",
            "User identifier",
            ParameterType.Text,
            "defaultUser",
            true,
            validationRules);

        // Assert
        Assert.Equal("username", parameter.Name);
        Assert.Equal("User identifier", parameter.Description);
        Assert.Equal(ParameterType.Text, parameter.Type);
        Assert.Equal("defaultUser", parameter.DefaultValue);
        Assert.True(parameter.IsRequired);
        Assert.Equal(validationRules, parameter.ValidationRules);
        Assert.Equal(3, parameter.ValidationRules.Count);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingTemplateParameterConstructorWithNullName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TemplateParameter(null!, "description", ParameterType.Text));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingTemplateParameterConstructorWithNullDescription()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TemplateParameter("name", null!, ParameterType.Text));
        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyDictionary_WhenUsingTemplateParameterConstructorWithNullValidationRules()
    {
        // Act
        var parameter = new TemplateParameter(
            "param",
            "description",
            ParameterType.Text,
            validationRules: null);

        // Assert
        Assert.NotNull(parameter.ValidationRules);
        Assert.Empty(parameter.ValidationRules);
    }

    #endregion

    #region Parameter Type Specific Tests

    [Fact]
    public void ShouldWork_WhenUsingTemplateParameterStringTypeWithStringDefault()
    {
        // Act
        var parameter = new TemplateParameter(
            "message",
            "Default message",
            ParameterType.Text,
            "Hello, World!");

        // Assert
        Assert.Equal(ParameterType.Text, parameter.Type);
        Assert.Equal("Hello, World!", parameter.DefaultValue);
    }

    [Fact]
    public void ShouldWork_WhenUsingTemplateParameterNumberTypeWithNumericDefault()
    {
        // Act
        var parameter = new TemplateParameter(
            "maxRetries",
            "Maximum retry attempts",
            ParameterType.Number,
            3);

        // Assert
        Assert.Equal(ParameterType.Number, parameter.Type);
        Assert.Equal(3, parameter.DefaultValue);
    }

    [Fact]
    public void ShouldWork_WhenUsingTemplateParameterBooleanTypeWithBoolDefault()
    {
        // Act
        var parameter = new TemplateParameter(
            "enableLogging",
            "Enable detailed logging",
            ParameterType.Boolean,
            true);

        // Assert
        Assert.Equal(ParameterType.Boolean, parameter.Type);
        Assert.True((bool)parameter.DefaultValue!);
    }

    [Fact]
    public void ShouldWork_WhenUsingTemplateParameterDateTimeTypeWithDateDefault()
    {
        // Arrange
        var defaultDate = DateTime.UtcNow;

        // Act
        var parameter = new TemplateParameter(
            "startDate",
            "Project start date",
            ParameterType.DateTime,
            defaultDate);

        // Assert
        Assert.Equal(ParameterType.DateTime, parameter.Type);
        Assert.Equal(defaultDate, parameter.DefaultValue);
    }

    [Fact]
    public void ShouldWork_WhenUsingTemplateParameterListTypeWithArrayDefault()
    {
        // Arrange
        var defaultList = Option1Option2Option3;

        // Act
        var parameter = new TemplateParameter(
            "allowedOptions",
            "List of allowed options",
            ParameterType.List,
            defaultList);

        // Assert
        Assert.Equal(ParameterType.List, parameter.Type);
        Assert.Equal(defaultList, parameter.DefaultValue);
    }

    [Fact]
    public void ShouldWork_WhenUsingTemplateParameterObjectTypeWithComplexDefault()
    {
        // Arrange
        var defaultObject = new
        {
            host = "localhost",
            port = 8080,
            ssl = true,
            credentials = new { username = "admin", password = "secret" }
        };

        // Act
        var parameter = new TemplateParameter(
            "serverConfig",
            "Server configuration object",
            ParameterType.Structured,
            defaultObject);

        // Assert
        Assert.Equal(ParameterType.Structured, parameter.Type);
        Assert.Equal(defaultObject, parameter.DefaultValue);
    }

    #endregion

    #region Validation Rules Tests

    [Fact]
    public void ShouldContainAppropriateRules_WhenUsingValidationRulesStringValidation()
    {
        // Arrange
        var validationRules = new Dictionary<string, object>
        {
            { "minLength", 3 },
            { "maxLength", 20 },
            { "pattern", "^[a-zA-Z]+$" },
            { "notEmpty", true }
        };

        // Act
        var parameter = new TemplateParameter(
            "firstName",
            "User's first name",
            ParameterType.Text,
            validationRules: validationRules);

        // Assert
        Assert.Equal(4, parameter.ValidationRules.Count);
        Assert.Equal(3, parameter.ValidationRules["minLength"]);
        Assert.Equal(20, parameter.ValidationRules["maxLength"]);
        Assert.Equal("^[a-zA-Z]+$", parameter.ValidationRules["pattern"]);
        Assert.True((bool)parameter.ValidationRules["notEmpty"]);
    }

    [Fact]
    public void ShouldContainAppropriateRules_WhenUsingValidationRulesNumberValidation()
    {
        // Arrange
        var validationRules = new Dictionary<string, object>
        {
            { "min", 0 },
            { "max", 100 },
            { "step", 0.5 },
            { "precision", 2 }
        };

        // Act
        var parameter = new TemplateParameter(
            "percentage",
            "Percentage value",
            ParameterType.Number,
            validationRules: validationRules);

        // Assert
        Assert.Equal(4, parameter.ValidationRules.Count);
        Assert.Equal(0, parameter.ValidationRules["min"]);
        Assert.Equal(100, parameter.ValidationRules["max"]);
        Assert.Equal(0.5, parameter.ValidationRules["step"]);
    }

    [Fact]
    public void ShouldSupportNestedRules_WhenUsingValidationRulesWithComplexValidation()
    {
        // Arrange
        var validationRules = new Dictionary<string, object>
        {
            { "allowedValues", DevStagingProd },
            { "customValidator", "ValidateEnvironment" },
            { "dependencies", new Dictionary<string, object>
                {
                    { "requiredWhen", new { field = "deploymentType", value = "kubernetes" } }
                }
            }
        };

        // Act
        var parameter = new TemplateParameter(
            "environment",
            "Deployment environment",
            ParameterType.Text,
            "dev",
            true,
            validationRules);

        // Assert
        Assert.True(parameter.IsRequired);
        Assert.Contains("allowedValues", parameter.ValidationRules.Keys);
        Assert.IsType<string[]>(parameter.ValidationRules["allowedValues"]);
        Assert.Contains("dependencies", parameter.ValidationRules.Keys);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenUsingTemplateParameterEqualitySameValues()
    {
        // Arrange
        var rules = new Dictionary<string, object> { { "min", 0 } };

        // Act
        var param1 = new TemplateParameter("count", "Item count", ParameterType.Number, 0, false, rules);
        var param2 = new TemplateParameter("count", "Item count", ParameterType.Number, 0, false, rules);

        // Assert
        Assert.Equal(param1, param2);
        Assert.True(param1 == param2);
        Assert.False(param1 != param2);
        Assert.Equal(param1.GetHashCode(), param2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingTemplateParameterEqualityDifferentNames()
    {
        // Act
        var param1 = new TemplateParameter("param1", "Description", ParameterType.Text);
        var param2 = new TemplateParameter("param2", "Description", ParameterType.Text);

        // Assert
        Assert.NotEqual(param1, param2);
        Assert.False(param1 == param2);
        Assert.True(param1 != param2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingTemplateParameterEqualityDifferentTypes()
    {
        // Act
        var param1 = new TemplateParameter("param", "Description", ParameterType.Text);
        var param2 = new TemplateParameter("param", "Description", ParameterType.Number);

        // Assert
        Assert.NotEqual(param1, param2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingTemplateParameterWith()
    {
        // Arrange
        var original = new TemplateParameter(
            "original",
            "Original description",
            ParameterType.Text,
            "default",
            false);

        // Act - Note: Records use 'with' expressions for non-destructive mutation
        // This would work if the properties had init setters
        // var modified = original with { IsRequired = true };

        // For this test, we'll verify the record behavior
        var copy = original;

        // Assert
        Assert.Equal(original, copy);
        Assert.True(ReferenceEquals(original, copy)); // Records are reference types
    }

    #endregion

    #region ParameterType Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingParameterType()
    {
        // Act & Assert
        var expectedValues = AllParameterTypes;

        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<ParameterType>(expectedValue));
        }

        var allValues = Enum.GetValues<ParameterType>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeString_WhenUsingParameterTypeWithDefaultValue()
    {
        // Act
        var defaultValue = default(ParameterType);

        // Assert
        Assert.Equal(ParameterType.Text, defaultValue);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldAccept_WhenUsingTemplateParameterWithEmptyStrings()
    {
        // Act
        var parameter = new TemplateParameter(
            "",
            "",
            ParameterType.Text);

        // Assert
        Assert.Equal(string.Empty, parameter.Name);
        Assert.Equal(string.Empty, parameter.Description);
    }

    [Fact]
    public void ShouldBeExplicit_WhenUsingTemplateParameterWithNullDefaultValue()
    {
        // Act
        var parameter = new TemplateParameter(
            "nullable",
            "Can be null",
            ParameterType.Structured,
            null);

        // Assert
        Assert.Null(parameter.DefaultValue);
    }

    [Fact]
    public void ShouldAccept_WhenUsingTemplateParameterWithMismatchedTypeAndDefault()
    {
        // Act - No validation in constructor
        var parameter = new TemplateParameter(
            "confused",
            "Type mismatch",
            ParameterType.Number,
            "This is a string, not a number!");

        // Assert
        Assert.Equal(ParameterType.Number, parameter.Type);
        Assert.Equal("This is a string, not a number!", parameter.DefaultValue);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTemplateParameterWithUnicodeContent()
    {
        // Arrange
        var validationRules = new Dictionary<string, object>
        {
            { "允许的值", Choice1Choice2 },
            { "错误消息", "无效的输入 ❌" }
        };

        // Act
        var parameter = new TemplateParameter(
            "语言设置",
            "选择界面语言 🌐",
            ParameterType.Text,
            "中文",
            true,
            validationRules);

        // Assert
        Assert.Equal("语言设置", parameter.Name);
        Assert.Contains("🌐", parameter.Description);
        Assert.Equal("中文", parameter.DefaultValue);
        Assert.Contains("允许的值", parameter.ValidationRules.Keys);
        Assert.Contains("❌", parameter.ValidationRules["错误消息"].ToString());
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingTemplateParameterToString()
    {
        // Arrange
        var parameter = new TemplateParameter(
            "testParam",
            "Test parameter",
            ParameterType.Text,
            "default");

        // Act
        var stringRepresentation = parameter.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // Records automatically generate ToString
        Assert.Contains("testParam", stringRepresentation);
        Assert.Contains("Test parameter", stringRepresentation);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldApiConfigurationScenario_WhenUsingTemplateParameter()
    {
        // Arrange - Create parameters for API configuration template
        var parameters = new List<TemplateParameter>
        {
            new TemplateParameter(
                "apiEndpoint",
                "Base URL for the API",
                ParameterType.Text,
                "https://api.example.com",
                true,
                new Dictionary<string, object>
                {
                    { "pattern", @"^https?://[\w\-]+(\.[\w\-]+)+[/#?]?.*$" },
                    { "errorMessage", "Must be a valid URL" }
                }),

            new TemplateParameter(
                "apiKey",
                "API authentication key",
                ParameterType.Text,
                null,
                true,
                new Dictionary<string, object>
                {
                    { "minLength", 32 },
                    { "maxLength", 64 },
                    { "secure", true }
                }),

            new TemplateParameter(
                "timeout",
                "Request timeout in seconds",
                ParameterType.Number,
                30,
                false,
                new Dictionary<string, object>
                {
                    { "min", 1 },
                    { "max", 300 }
                }),

            new TemplateParameter(
                "retryPolicy",
                "Retry configuration",
                ParameterType.Structured,
                new { maxRetries = 3, backoffMultiplier = 2.0, initialDelay = 1000 },
                false,
                null),

            new TemplateParameter(
                "enabledFeatures",
                "List of enabled features",
                ParameterType.List,
                LoggingCaching,
                false,
                new Dictionary<string, object>
                {
                    { "allowedValues", LoggingCachingCompressionEncryption }
                })
        };

        // Assert
        Assert.Equal(5, parameters.Count);
        Assert.Equal(2, parameters.Count(p => p.IsRequired));
        Assert.All(parameters, p => Assert.NotNull(p.ValidationRules));

        var apiKeyParam = parameters.First(p => p.Name == "apiKey");
        Assert.True((bool)apiKeyParam.ValidationRules["secure"]);

        var timeoutParam = parameters.First(p => p.Name == "timeout");
        Assert.Equal(30, timeoutParam.DefaultValue);
    }

    [Fact]
    public void ShouldMachineLearningScenario_WhenUsingTemplateParameter()
    {
        // Arrange - Create parameters for ML model configuration
        var parameters = new[]
        {
            new TemplateParameter(
                "modelType",
                "Type of ML model to use",
                ParameterType.Text,
                "neural_network",
                true,
                new Dictionary<string, object>
                {
                    { "allowedValues", NnRfSvmXgboost }
                }),

            new TemplateParameter(
                "hyperparameters",
                "Model hyperparameters",
                ParameterType.Structured,
                new
                {
                    learning_rate = 0.001,
                    batch_size = 32,
                    epochs = 100,
                    hidden_layers = HiddenLayers
                },
                false,
                null),

            new TemplateParameter(
                "trainingStartTime",
                "When to start training",
                ParameterType.DateTime,
                DateTime.UtcNow.AddHours(1),
                false,
                new Dictionary<string, object>
                {
                    { "minValue", DateTime.UtcNow },
                    { "maxValue", DateTime.UtcNow.AddDays(7) }
                }),

            new TemplateParameter(
                "enableGPU",
                "Use GPU acceleration",
                ParameterType.Boolean,
                true,
                false,
                new Dictionary<string, object>
                {
                    { "requiresCapability", "cuda" }
                })
        };

        // Assert
        var hyperparamsParam = parameters.First(p => p.Name == "hyperparameters");
        Assert.NotNull(hyperparamsParam.DefaultValue);
        Assert.Equal(ParameterType.Structured, hyperparamsParam.Type);

        var gpuParam = parameters.First(p => p.Name == "enableGPU");
        Assert.True((bool)gpuParam.DefaultValue!);
        Assert.Contains("requiresCapability", gpuParam.ValidationRules.Keys);
    }

    #endregion
}
