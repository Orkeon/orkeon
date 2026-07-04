using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Orchestration;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Registration tests for the opt-in execution-state persistence (R3.8):
/// default off (backward compatible), explicit code-first activation,
/// configuration-driven activation, and the SQLite checkpointing store registration.
/// </summary>
public class ExecutionStatePersistenceRegistrationTests
{
    [Fact]
    public void PersistenceOptions_DefaultToDisabled_WhenNotConfigured()
    {
        // Arrange — plain state management, no persistence registration
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrewExecutionStateManagement();

        using var sp = services.BuildServiceProvider();

        // Act
        var options = sp.GetRequiredService<IOptions<CrewExecutionStatePersistenceOptions>>().Value;
        var manager = sp.GetService<ICrewExecutionStateManager>();

        // Assert — backward-compatible default: in-memory only
        Assert.False(options.Enabled);
        Assert.NotNull(manager);
        Assert.IsType<ScopedCrewExecutionStateManager>(manager);
    }

    [Fact]
    public void AddCrewExecutionStatePersistence_EnablesPersistence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrewExecutionStateManagement();
        services.AddOrkeonCheckpointing(); // provides the InMemoryStateStore
        services.AddCrewExecutionStatePersistence();

        using var sp = services.BuildServiceProvider();

        // Act
        var options = sp.GetRequiredService<IOptions<CrewExecutionStatePersistenceOptions>>().Value;

        // Assert
        Assert.True(options.Enabled);
        Assert.False(options.DeleteFromStoreOnArchive);
        Assert.NotNull(sp.GetService<IStateStore>());
    }

    [Fact]
    public void AddCrewExecutionStatePersistence_AppliesExtraConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCrewExecutionStatePersistence(o => o.DeleteFromStoreOnArchive = true);

        using var sp = services.BuildServiceProvider();

        // Act
        var options = sp.GetRequiredService<IOptions<CrewExecutionStatePersistenceOptions>>().Value;

        // Assert
        Assert.True(options.Enabled);
        Assert.True(options.DeleteFromStoreOnArchive);
    }

    [Fact]
    public void AddCrewExecutionStatePersistence_BindsFromConfigurationSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:ExecutionState:Persistence:Enabled"] = "true",
                ["Orkeon:ExecutionState:Persistence:DeleteFromStoreOnArchive"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCrewExecutionStatePersistence(configuration);

        using var sp = services.BuildServiceProvider();

        // Act
        var options = sp.GetRequiredService<IOptions<CrewExecutionStatePersistenceOptions>>().Value;

        // Assert
        Assert.True(options.Enabled);
        Assert.True(options.DeleteFromStoreOnArchive);
    }

    [Fact]
    public void AddCrewExecutionStatePersistence_ConfigurationCanKeepItDisabled()
    {
        // Arrange — section present but explicitly disabled
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:ExecutionState:Persistence:Enabled"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCrewExecutionStatePersistence(configuration);

        using var sp = services.BuildServiceProvider();

        // Act
        var options = sp.GetRequiredService<IOptions<CrewExecutionStatePersistenceOptions>>().Value;

        // Assert
        Assert.False(options.Enabled);
    }

    [Fact]
    public void AddOrkeonSqliteCheckpointing_RegistersSqliteStoreAndCheckpointServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonSqliteCheckpointing("Data Source=:memory:");

        using var sp = services.BuildServiceProvider();

        // Act
        var store = sp.GetService<IStateStore>();

        // Assert
        Assert.NotNull(store);
        Assert.IsType<SqliteStateStore>(store);
        Assert.NotNull(sp.GetService<ICheckpointManager>());
        Assert.NotNull(sp.GetService<IResumeEngine>());
    }

    [Fact]
    public void AddOrkeonSqliteCheckpointing_Throws_WhenConnectionStringIsBlank()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddOrkeonSqliteCheckpointing("  "));
    }
}
