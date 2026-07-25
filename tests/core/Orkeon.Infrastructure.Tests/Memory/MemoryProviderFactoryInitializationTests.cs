using Orkeon.Application.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// RAG-01/C5: <see cref="MemoryProviderFactory.CreateAndInitializeAsync"/> must really
/// initialize the provider — the <c>RetentionPeriod</c>/<c>MaxItems</c>/<c>KeyPrefix</c>
/// options used to be dead configuration (never applied). These tests prove the DTO options
/// are mapped to the Domain <c>MemoryProviderConfig</c> and reach the provider.
/// </summary>
public class MemoryProviderFactoryInitializationTests
{
    private readonly MemoryProviderFactory _factory = new(new FakeFileSystemService());

    [Fact]
    public async Task CreateAndInitializeAsync_ShouldApplyRetentionMaxItemsAndKeyPrefix()
    {
        // Arrange
        var config = new MemoryProviderConfigDto(
            "inmemory",
            ConnectionString: string.Empty,
            Options: new Dictionary<string, object>
            {
                ["RetentionPeriod"] = "7.00:00:00",
                ["MaxItems"] = 250,
                ["KeyPrefix"] = "crew42:",
            });

        // Act
        var provider = await _factory.CreateAndInitializeAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the configuration actually reached the provider.
        var configurable = Assert.IsType<MemoryProviderBase>(provider, exactMatch: false);
        Assert.NotNull(configurable.Configuration);
        Assert.Equal(TimeSpan.FromDays(7), configurable.Configuration!.RetentionPeriod);
        Assert.Equal(250, configurable.Configuration.MaxItems);
        Assert.Equal("crew42:", configurable.Configuration.KeyPrefix);
        Assert.Equal("inmemory", configurable.Configuration.ProviderType);
    }

    [Fact]
    public async Task CreateAndInitializeAsync_ShouldSupportTypedOptionValues()
    {
        // Arrange — TimeSpan instance, numeric day count fallback, string MaxItems.
        var config = new MemoryProviderConfigDto(
            "inmemory",
            ConnectionString: "conn-under-test",
            Options: new Dictionary<string, object>
            {
                ["RetentionPeriod"] = TimeSpan.FromHours(36),
                ["MaxItems"] = "1234",
            });

        // Act
        var provider = await _factory.CreateAndInitializeAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var configurable = Assert.IsType<MemoryProviderBase>(provider, exactMatch: false);
        Assert.NotNull(configurable.Configuration);
        Assert.Equal(TimeSpan.FromHours(36), configurable.Configuration!.RetentionPeriod);
        Assert.Equal(1234, configurable.Configuration.MaxItems);
        Assert.Null(configurable.Configuration.KeyPrefix);
        Assert.Equal("conn-under-test", configurable.Configuration.ConnectionString);
    }

    [Fact]
    public async Task CreateAndInitializeAsync_ShouldSupportNumericDayCountRetention()
    {
        // Arrange
        var config = new MemoryProviderConfigDto(
            "inmemory",
            ConnectionString: string.Empty,
            Options: new Dictionary<string, object> { ["RetentionPeriod"] = 3 });

        // Act
        var provider = await _factory.CreateAndInitializeAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var configurable = Assert.IsType<MemoryProviderBase>(provider, exactMatch: false);
        Assert.Equal(TimeSpan.FromDays(3), configurable.Configuration!.RetentionPeriod);
    }

    [Fact]
    public async Task CreateAndInitializeAsync_WithoutOptions_ShouldStillInitialize()
    {
        // Arrange — no options at all: the provider must still be initialized (non-null
        // Configuration), just with no retention/max-items/key-prefix values.
        var config = new MemoryProviderConfigDto("inmemory", ConnectionString: string.Empty);

        // Act
        var provider = await _factory.CreateAndInitializeAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var configurable = Assert.IsType<MemoryProviderBase>(provider, exactMatch: false);
        Assert.NotNull(configurable.Configuration);
        Assert.Null(configurable.Configuration!.RetentionPeriod);
        Assert.Null(configurable.Configuration.MaxItems);
        Assert.Null(configurable.Configuration.KeyPrefix);
    }

    [Fact]
    public async Task CreateAndInitializeAsync_ShouldCarryOptionsThroughAsSettings()
    {
        // Arrange — unrecognized options must remain available to providers via Settings.
        var config = new MemoryProviderConfigDto(
            "inmemory",
            ConnectionString: string.Empty,
            Options: new Dictionary<string, object>
            {
                ["KeyPrefix"] = "p:",
                ["CustomOption"] = "custom-value",
            });

        // Act
        var provider = await _factory.CreateAndInitializeAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var configurable = Assert.IsType<MemoryProviderBase>(provider, exactMatch: false);
        Assert.Equal("custom-value", configurable.Configuration!.Settings["CustomOption"]);
        Assert.Equal("p:", configurable.Configuration.Settings["KeyPrefix"]);
    }
}
