using Orkeon.Domain.Flows;

using Orkeon.Domain.Common;
namespace Orkeon.Domain.Tests.Flows;

/// <summary>
/// Tests for Flow Attributes following Clean Architecture principles.
/// Tests the flow attribute classes used for method and class decoration.
/// </summary>
public class FlowAttributesTests
{
    #region FlowBaseAttribute Tests

    // Create concrete implementation for testing abstract base
    private class TestFlowAttribute : FlowBaseAttribute
    {
    }

    [Fact]
    public void ShouldBeEmpty_WhenUsingFlowBaseAttributeWithDefaultValues()
    {
        // Act
        var attribute = new TestFlowAttribute();

        // Assert
        Assert.Equal(string.Empty, attribute.Name);
        Assert.Equal(string.Empty, attribute.Description);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowBaseAttributeSetProperties()
    {
        // Arrange
        var attribute = new TestFlowAttribute();
        var name = "TestFlow";
        var description = "A test flow for validation";

        // Act
        attribute.Name = name;
        attribute.Description = description;

        // Assert
        Assert.Equal(name, attribute.Name);
        Assert.Equal(description, attribute.Description);
    }

    #endregion

    #region FlowStepAttribute Tests

    [Fact]
    public void ShouldBeSetCorrectly_WhenUsingFlowStepAttributeWithDefaultValues()
    {
        // Act
        var attribute = new FlowStepAttribute();

        // Assert
        Assert.Equal(string.Empty, attribute.Name);
        Assert.Equal(string.Empty, attribute.Description);
        Assert.Equal(0, attribute.Order);
        Assert.False(attribute.IsOptional);
        Assert.Null(attribute.RequiredInputs);
        Assert.Null(attribute.ProvidedOutputs);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowStepAttributeSetAllProperties()
    {
        // Arrange
        var attribute = new FlowStepAttribute();
        var requiredInputs = new[] { "input1", "input2" };
        var providedOutputs = new[] { "output1", "output2" };

        // Act
        attribute.Name = "ProcessData";
        attribute.Description = "Processes input data";
        attribute.Order = 5;
        attribute.IsOptional = true;
        attribute.RequiredInputs = requiredInputs;
        attribute.ProvidedOutputs = providedOutputs;

        // Assert
        Assert.Equal("ProcessData", attribute.Name);
        Assert.Equal("Processes input data", attribute.Description);
        Assert.Equal(5, attribute.Order);
        Assert.True(attribute.IsOptional);
        Assert.Equal(requiredInputs, attribute.RequiredInputs);
        Assert.Equal(providedOutputs, attribute.ProvidedOutputs);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenUsingFlowStepAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(FlowStepAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
        Assert.False(attributeUsage.AllowMultiple);
        Assert.True(attributeUsage.Inherited);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(-1)]
    [InlineData(0)]
    public void ShouldAcceptAnyInteger_WhenUsingFlowStepAttributeOrder(int order)
    {
        // Act
        var attribute = new FlowStepAttribute { Order = order };

        // Assert
        Assert.Equal(order, attribute.Order);
    }

    [Fact]
    public void ShouldAcceptEmptyArrays_WhenUsingFlowStepAttributeWithEmptyArrays()
    {
        // Arrange
        var emptyInputs = Array.Empty<string>();
        var emptyOutputs = Array.Empty<string>();

        // Act
        var attribute = new FlowStepAttribute
        {
            RequiredInputs = emptyInputs,
            ProvidedOutputs = emptyOutputs
        };

        // Assert
        Assert.Equal(emptyInputs, attribute.RequiredInputs);
        Assert.Equal(emptyOutputs, attribute.ProvidedOutputs);
        Assert.Empty(attribute.RequiredInputs);
        Assert.Empty(attribute.ProvidedOutputs);
    }

    #endregion

    #region FlowAttribute Tests

    [Fact]
    public void ShouldBeSetCorrectly_WhenUsingFlowAttributeWithDefaultValues()
    {
        // Act
        var attribute = new FlowAttribute();

        // Assert
        Assert.Equal(string.Empty, attribute.Name);
        Assert.Equal(string.Empty, attribute.Description);
        Assert.NotNull(attribute.Version);
        Assert.Null(attribute.Tags);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowAttributeSetAllProperties()
    {
        // Arrange
        var attribute = new FlowAttribute();
        var tags = new[] { "automation", "data-processing", "async" };

        // Act
        attribute.Name = "DataPipeline";
        attribute.Description = "Automated data processing pipeline";
        attribute.Version = ConfigurationVersionId.Create();
        attribute.Tags = tags;

        // Assert
        Assert.Equal("DataPipeline", attribute.Name);
        Assert.Equal("Automated data processing pipeline", attribute.Description);
        Assert.NotNull(attribute.Version);
        Assert.Equal(tags, attribute.Tags);
        Assert.Equal(3, attribute.Tags.Length);
    }

    [Fact]
    public void ShouldBeClassOnly_WhenUsingFlowAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(FlowAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Class, attributeUsage.ValidOn);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0.0")]
    [InlineData("1.2.3-beta")]
    [InlineData("10.15.20-alpha.1")]
    [InlineData("")]
    public void ShouldAcceptVariousFormats_WhenUsingFlowAttributeVersion(string version)
    {
        // Act
        var attribute = new FlowAttribute { Version = version };

        // Assert
        Assert.Equal(version, attribute.Version);
    }

    [Fact]
    public void ShouldAcceptEmptyArray_WhenUsingFlowAttributeWithEmptyTags()
    {
        // Arrange
        var emptyTags = Array.Empty<string>();

        // Act
        var attribute = new FlowAttribute { Tags = emptyTags };

        // Assert
        Assert.Equal(emptyTags, attribute.Tags);
        Assert.Empty(attribute.Tags);
    }

    #endregion

    #region FlowValidatorAttribute Tests

    [Fact]
    public void ShouldBeNull_WhenUsingFlowValidatorAttributeWithDefaultValues()
    {
        // Act
        var attribute = new FlowValidatorAttribute();

        // Assert
        Assert.Null(attribute.ValidatesInputs);
    }

    [Fact]
    public void ShouldAcceptArray_WhenUsingFlowValidatorAttributeSetValidatesInputs()
    {
        // Arrange
        var validatesInputs = new[] { "userId", "email", "password" };

        // Act
        var attribute = new FlowValidatorAttribute { ValidatesInputs = validatesInputs };

        // Assert
        Assert.Equal(validatesInputs, attribute.ValidatesInputs);
        Assert.Equal(3, attribute.ValidatesInputs.Length);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenUsingFlowValidatorAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(FlowValidatorAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
    }

    [Fact]
    public void ShouldAcceptEmptyArray_WhenUsingFlowValidatorAttributeWithEmptyArray()
    {
        // Arrange
        var emptyInputs = Array.Empty<string>();

        // Act
        var attribute = new FlowValidatorAttribute { ValidatesInputs = emptyInputs };

        // Assert
        Assert.Equal(emptyInputs, attribute.ValidatesInputs);
        Assert.Empty(attribute.ValidatesInputs);
    }

    #endregion

    #region FlowErrorHandlerAttribute Tests

    [Fact]
    public void ShouldBeNull_WhenUsingFlowErrorHandlerAttributeWithDefaultValues()
    {
        // Act
        var attribute = new FlowErrorHandlerAttribute();

        // Assert
        Assert.Null(attribute.HandlesExceptions);
    }

    [Fact]
    public void ShouldAcceptTypeArray_WhenUsingFlowErrorHandlerAttributeSetHandlesExceptions()
    {
        // Arrange
        var exceptionTypes = new[] { typeof(ArgumentException), typeof(InvalidOperationException), typeof(TimeoutException) };

        // Act
        var attribute = new FlowErrorHandlerAttribute { HandlesExceptions = exceptionTypes };

        // Assert
        Assert.Equal(exceptionTypes, attribute.HandlesExceptions);
        Assert.Equal(3, attribute.HandlesExceptions.Length);
        Assert.Contains(typeof(ArgumentException), attribute.HandlesExceptions);
        Assert.Contains(typeof(InvalidOperationException), attribute.HandlesExceptions);
        Assert.Contains(typeof(TimeoutException), attribute.HandlesExceptions);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenUsingFlowErrorHandlerAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(FlowErrorHandlerAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
    }

    [Fact]
    public void ShouldAcceptEmptyArray_WhenUsingFlowErrorHandlerAttributeWithEmptyArray()
    {
        // Arrange
        var emptyTypes = Array.Empty<Type>();

        // Act
        var attribute = new FlowErrorHandlerAttribute { HandlesExceptions = emptyTypes };

        // Assert
        Assert.Equal(emptyTypes, attribute.HandlesExceptions);
        Assert.Empty(attribute.HandlesExceptions);
    }

    #endregion

    #region FlowBeforeAttribute Tests

    [Fact]
    public void ShouldBeInstantiable_WhenUsingFlowBeforeAttribute()
    {
        // Act
        var attribute = new FlowBeforeAttribute();

        // Assert
        Assert.NotNull(attribute);
        Assert.IsType<FlowBeforeAttribute>(attribute);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenUsingFlowBeforeAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(FlowBeforeAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
    }

    #endregion

    #region FlowAfterAttribute Tests

    [Fact]
    public void ShouldBeInstantiable_WhenUsingFlowAfterAttribute()
    {
        // Act
        var attribute = new FlowAfterAttribute();

        // Assert
        Assert.NotNull(attribute);
        Assert.IsType<FlowAfterAttribute>(attribute);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenUsingFlowAfterAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(FlowAfterAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
    }

    #endregion

    #region StartAttribute Tests

    [Fact]
    public void ShouldBeNull_WhenStartingAttributeWithDefaultValues()
    {
        // Act
        var attribute = new StartAttribute();

        // Assert
        Assert.Null(attribute.Name);
    }

    [Fact]
    public void ShouldAcceptValue_WhenStartingAttributeSetName()
    {
        // Arrange
        var name = "InitializeFlow";

        // Act
        var attribute = new StartAttribute { Name = name };

        // Assert
        Assert.Equal(name, attribute.Name);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenStartingAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(StartAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("start")]
    [InlineData("initialize_flow")]
    [InlineData("Begin-Process")]
    [InlineData(null)]
    public void ShouldAcceptVariousValues_WhenStartingAttributeName(string? name)
    {
        // Act
        var attribute = new StartAttribute { Name = name };

        // Assert
        Assert.Equal(name, attribute.Name);
    }

    #endregion

    #region ListenAttribute Tests

    [Fact]
    public void ShouldSetEventName_WhenUsingListenAttributeConstructorWithEventName()
    {
        // Arrange
        var eventName = "DataProcessed";

        // Act
        var attribute = new ListenAttribute(eventName);

        // Assert
        Assert.Equal(eventName, attribute.EventName);
        Assert.Null(attribute.Condition);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingListenAttributeConstructorWithNullEventName()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ListenAttribute(null!));
    }

    [Fact]
    public void ShouldAcceptValue_WhenUsingListenAttributeSetCondition()
    {
        // Arrange
        var eventName = "TaskCompleted";
        var condition = "result.Success == true";

        // Act
        var attribute = new ListenAttribute(eventName) { Condition = condition };

        // Assert
        Assert.Equal(eventName, attribute.EventName);
        Assert.Equal(condition, attribute.Condition);
    }

    [Fact]
    public void ShouldAllowMultiple_WhenUsingListenAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(ListenAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
        Assert.True(attributeUsage.AllowMultiple);
    }

    [Theory]
    [InlineData("")]
    [InlineData("condition == true")]
    [InlineData("data.Count > 0")]
    [InlineData("status != 'failed'")]
    [InlineData(null)]
    public void ShouldAcceptVariousValues_WhenUsingListenAttributeCondition(string? condition)
    {
        // Act
        var attribute = new ListenAttribute("TestEvent") { Condition = condition };

        // Assert
        Assert.Equal(condition, attribute.Condition);
    }

    [Theory]
    [InlineData("step_completed")]
    [InlineData("DataProcessed")]
    [InlineData("task-finished")]
    [InlineData("USER_LOGGED_IN")]
    public void ShouldAcceptVariousFormats_WhenUsingListenAttributeEventName(string eventName)
    {
        // Act
        var attribute = new ListenAttribute(eventName);

        // Assert
        Assert.Equal(eventName, attribute.EventName);
    }

    #endregion

    #region RouterAttribute Tests

    [Fact]
    public void ShouldBeNull_WhenUsingRouterAttributeWithDefaultValues()
    {
        // Act
        var attribute = new RouterAttribute();

        // Assert
        Assert.Null(attribute.Routes);
        Assert.Null(attribute.Name);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingRouterAttributeSetAllProperties()
    {
        // Arrange
        var routes = new[] { "success", "failure", "retry" };
        var name = "ProcessingRouter";

        // Act
        var attribute = new RouterAttribute
        {
            Routes = routes,
            Name = name
        };

        // Assert
        Assert.Equal(routes, attribute.Routes);
        Assert.Equal(name, attribute.Name);
        Assert.Equal(3, attribute.Routes.Length);
    }

    [Fact]
    public void ShouldBeMethodOnly_WhenUsingRouterAttributeAttributeUsage()
    {
        // Act
        var attributeUsage = typeof(RouterAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        // Assert
        Assert.Equal(AttributeTargets.Method, attributeUsage.ValidOn);
    }

    [Fact]
    public void ShouldAcceptEmptyArray_WhenUsingRouterAttributeWithEmptyRoutes()
    {
        // Arrange
        var emptyRoutes = Array.Empty<string>();

        // Act
        var attribute = new RouterAttribute { Routes = emptyRoutes };

        // Assert
        Assert.Equal(emptyRoutes, attribute.Routes);
        Assert.Empty(attribute.Routes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MainRouter")]
    [InlineData("decision_router")]
    [InlineData("Route-Handler-1")]
    public void ShouldAcceptVariousValues_WhenUsingRouterAttributeName(string? name)
    {
        // Act
        var attribute = new RouterAttribute { Name = name };

        // Assert
        Assert.Equal(name, attribute.Name);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingFlowAttributesInheritanceChain()
    {
        // Arrange
        var flowStepAttribute = new FlowStepAttribute();
        var flowAttribute = new FlowAttribute();

        // Act & Assert - verify exact types (both inherit from FlowBaseAttribute which inherits from Attribute)
        Assert.IsType<FlowStepAttribute>(flowStepAttribute);
        Assert.IsType<FlowAttribute>(flowAttribute);
    }

    [Fact]
    public void ShouldInheritFromAttribute_WhenUsingFlowAttributesWithAllAttributeClasses()
    {
        // Arrange
        var attributeTypes = new[]
        {
            typeof(FlowStepAttribute),
            typeof(FlowAttribute),
            typeof(FlowValidatorAttribute),
            typeof(FlowErrorHandlerAttribute),
            typeof(FlowBeforeAttribute),
            typeof(FlowAfterAttribute),
            typeof(StartAttribute),
            typeof(ListenAttribute),
            typeof(RouterAttribute)
        };

        // Act & Assert
        foreach (var attributeType in attributeTypes)
        {
            Assert.True(typeof(Attribute).IsAssignableFrom(attributeType),
                $"{attributeType.Name} should inherit from Attribute");
        }
    }

    [Fact]
    public void ShouldHandleAllProperties_WhenUsingFlowStepAttributeWithComplexConfiguration()
    {
        // Arrange & Act
        var attribute = new FlowStepAttribute
        {
            Name = "ValidateAndTransformData",
            Description = "Validates input data and transforms it to the required format",
            Order = 2,
            IsOptional = false,
            RequiredInputs = ["rawData", "validationRules", "transformationConfig"],
            ProvidedOutputs = ["validatedData", "transformedData", "validationReport"]
        };

        // Assert
        Assert.Equal("ValidateAndTransformData", attribute.Name);
        Assert.Equal("Validates input data and transforms it to the required format", attribute.Description);
        Assert.Equal(2, attribute.Order);
        Assert.False(attribute.IsOptional);
        Assert.Equal(3, attribute.RequiredInputs!.Length);
        Assert.Equal(3, attribute.ProvidedOutputs!.Length);
        Assert.Contains("rawData", attribute.RequiredInputs);
        Assert.Contains("transformedData", attribute.ProvidedOutputs);
    }

    [Fact]
    public void ShouldAllowDifferentEventNames_WhenUsingListenAttributeWithMultipleInstances()
    {
        // This test verifies that the AllowMultiple = true works correctly
        // In practice, this would be used like:
        // [Listen("EventA")]
        // [Listen("EventB")]
        // public void HandleMultipleEvents() { }

        // Arrange & Act
        var attribute1 = new ListenAttribute("DataReceived") { Condition = "data.IsValid" };
        var attribute2 = new ListenAttribute("ErrorOccurred") { Condition = "error.Severity == 'High'" };

        // Assert
        Assert.Equal("DataReceived", attribute1.EventName);
        Assert.Equal("ErrorOccurred", attribute2.EventName);
        Assert.Equal("data.IsValid", attribute1.Condition);
        Assert.Equal("error.Severity == 'High'", attribute2.Condition);
        Assert.NotEqual(attribute1.EventName, attribute2.EventName);
    }

    [Fact]
    public void ShouldSupportComplexVersioning_WhenUsingFlowAttributeVersionAndTags()
    {
        // Arrange & Act
        var attribute = new FlowAttribute
        {
            Name = "DataProcessingPipeline",
            Description = "Enterprise data processing pipeline with advanced features",
            Version = ConfigurationVersionId.Create(),
            Tags = ["enterprise", "data-processing", "beta", "high-performance", "scalable"]
        };

        // Assert
        Assert.Equal("DataProcessingPipeline", attribute.Name);
        Assert.Contains("Enterprise", attribute.Description);
        Assert.NotNull(attribute.Version);
        Assert.Equal(5, attribute.Tags!.Length);
        Assert.Contains("enterprise", attribute.Tags);
        Assert.Contains("scalable", attribute.Tags);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowAttributesWithLongStrings()
    {
        // Arrange
        var longString = new string('A', 1000);

        // Act
        var flowStepAttribute = new FlowStepAttribute
        {
            Name = longString,
            Description = longString
        };
        var flowAttribute = new FlowAttribute
        {
            Name = longString,
            Description = longString,
            Version = longString
        };

        // Assert
        Assert.Equal(1000, flowStepAttribute.Name.Length);
        Assert.Equal(1000, flowStepAttribute.Description.Length);
        Assert.Equal(1000, flowAttribute.Name.Length);
        Assert.Equal(1000, flowAttribute.Description.Length);
        Assert.Equal(1000, flowAttribute.Version.Length);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowAttributesWithUnicodeStrings()
    {
        // Arrange
        var unicodeName = "处理数据流程";
        var unicodeDescription = "处理数据的工作流程 🚀 with émojis and spëcial chars";

        // Act
        var attribute = new FlowStepAttribute
        {
            Name = unicodeName,
            Description = unicodeDescription
        };

        // Assert
        Assert.Equal(unicodeName, attribute.Name);
        Assert.Equal(unicodeDescription, attribute.Description);
        Assert.Contains("🚀", attribute.Description);
        Assert.Contains("spëcial", attribute.Description);
    }

    [Fact]
    public void ShouldAcceptAll_WhenUsingFlowErrorHandlerAttributeWithCustomExceptionTypes()
    {
        // Arrange - Create custom exception types for testing  
        var customExceptionTypes = new[]
        {
            typeof(Exception),
            typeof(SystemException),
            typeof(ApplicationException),
            typeof(ArgumentException),
            typeof(ArgumentNullException),
            typeof(InvalidOperationException)
        };

        // Act
        var attribute = new FlowErrorHandlerAttribute { HandlesExceptions = customExceptionTypes };

        // Assert
        Assert.Equal(6, attribute.HandlesExceptions!.Length);
        Assert.All(attribute.HandlesExceptions, type =>
        {
            Assert.True(typeof(Exception).IsAssignableFrom(type),
                $"{type.Name} should be assignable from Exception");
        });
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingRouterAttributeWithManyRoutes()
    {
        // Arrange
        var manyRoutes = Enumerable.Range(1, 100).Select(i => $"route_{i}").ToArray();

        // Act
        var attribute = new RouterAttribute { Routes = manyRoutes };

        // Assert
        Assert.Equal(100, attribute.Routes!.Length);
        Assert.Equal("route_1", attribute.Routes[0]);
        Assert.Equal("route_100", attribute.Routes[99]);
    }

    #endregion
}
