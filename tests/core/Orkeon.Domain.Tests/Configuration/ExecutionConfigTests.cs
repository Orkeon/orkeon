using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for the <see cref="ExecutionConfig"/> immutable value object.
/// (The former <c>ExecutionConfigExtensions</c> static bridge has been removed.)
/// </summary>
public class ExecutionConfigTests
{
    [Fact]
    public void ShouldCreateWithDefaults_WhenUsingExecutionConfigDirectly()
    {
        // Act
        var config = new ExecutionConfig();

        // Assert
        Assert.Equal(0, config.MaxRPM);
        Assert.Null(config.ManagerLlm);
        Assert.Equal(10, config.MaxConcurrentTasks);
        Assert.Equal(TimeoutStandard, config.DefaultTimeout);
    }

    [Fact]
    public void ShouldSupportWithSyntax_WhenModifyingExecutionConfig()
    {
        // Arrange
        var config = new ExecutionConfig();

        // Act
        var modified = config with { MaxRPM = 100, ManagerLlm = ModelClaude3 };

        // Assert
        Assert.Equal(100, modified.MaxRPM);
        Assert.Equal(ModelClaude3, modified.ManagerLlm);
        Assert.Equal(0, config.MaxRPM); // Original unchanged
    }

    [Fact]
    public void ShouldStoreValuePerExecutionConfigInstance_WhenUsingExpectedBehavior()
    {
        // Arrange & Act
        var config1 = new ExecutionConfig { MaxRPM = 10, ManagerLlm = "model-a" };
        var config2 = new ExecutionConfig { MaxRPM = 20, ManagerLlm = "model-b" };

        // Assert - each instance has its own values
        Assert.Equal(10, config1.MaxRPM);
        Assert.Equal("model-a", config1.ManagerLlm);
        Assert.Equal(20, config2.MaxRPM);
        Assert.Equal("model-b", config2.ManagerLlm);
    }
}
