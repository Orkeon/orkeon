using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for the <see cref="ExecutionConfig"/> immutable value object.
/// (The former <c>ExecutionConfigExtensions</c> static bridge has been removed, and so has the
/// <c>ManagerLlm</c> string no code read — a crew's manager runs on <c>Crew.ManagerLlm</c> or its
/// manager agent's profile, GAP-19.)
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
        Assert.Empty(config.ExecutorSettings);
        Assert.Equal(10, config.MaxConcurrentTasks);
        Assert.Equal(TimeoutStandard, config.DefaultTimeout);
    }

    [Fact]
    public void ShouldSupportWithSyntax_WhenModifyingExecutionConfig()
    {
        // Arrange
        var config = new ExecutionConfig();

        // Act
        var modified = config with { MaxRPM = 100, MaxRetries = 7 };

        // Assert
        Assert.Equal(100, modified.MaxRPM);
        Assert.Equal(7, modified.MaxRetries);
        Assert.Equal(0, config.MaxRPM); // Original unchanged
    }

    [Fact]
    public void ShouldStoreValuePerExecutionConfigInstance_WhenUsingExpectedBehavior()
    {
        // Arrange & Act
        var config1 = new ExecutionConfig { MaxRPM = 10, MaxRetries = 1 };
        var config2 = new ExecutionConfig { MaxRPM = 20, MaxRetries = 2 };

        // Assert - each instance has its own values
        Assert.Equal(10, config1.MaxRPM);
        Assert.Equal(1, config1.MaxRetries);
        Assert.Equal(20, config2.MaxRPM);
        Assert.Equal(2, config2.MaxRetries);
    }
}
