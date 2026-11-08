using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

/// <summary>
/// FWK-01: the <c>Orkeon:Checkpointing</c> section chooses the store. The in-memory store is
/// registered first by <c>AddOrkeonInfrastructure</c>; the chosen one must win whatever the order.
/// </summary>
public sealed class CheckpointingConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => $"Orkeon:Checkpointing:{p.Key}", p => (string?)p.Value))
            .Build();

    private static ServiceProvider Provider(IConfiguration configuration, bool memoryFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/state", FileAccessRights.ReadWrite));
        if (memoryFirst)
        {
            services.AddOrkeonCheckpointing(); // what AddOrkeonInfrastructure does
            services.AddOrkeonCheckpointing(configuration);
        }
        else
        {
            services.AddOrkeonCheckpointing(configuration);
            services.AddOrkeonCheckpointing();
        }

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "FWK-01")]
    public async Task ShouldBackTheCheckpointManagerWithSqlite_WhateverTheRegistrationOrder(bool memoryFirst)
    {
        using var sp = Provider(
            Config(("Enabled", "true"), ("Store", "sqlite"), ("ConnectionString", "Data Source=:memory:")), memoryFirst);

        var store = sp.GetRequiredService<IStateStore>();
        var manager = sp.GetRequiredService<ICheckpointManager>();
        var id = ExecutionId.New();
        await manager.StartSessionAsync(id, "crew@/crew/crew.yaml", TestContext.Current.CancellationToken);

        Assert.IsType<SqliteStateStore>(store);
        var saved = await store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("crew@/crew/crew.yaml", saved.CrewKey);
        Assert.NotNull(sp.GetRequiredService<IResumeEngine>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "FWK-01")]
    public void ShouldReplaceTheInMemoryStore_WhateverTheOrder_WithTheSqliteAndPostgresOverloads(bool memoryFirst)
    {
        foreach (var register in new Action<IServiceCollection>[]
        {
            s => s.AddOrkeonSqliteCheckpointing("Data Source=:memory:"),
            s => s.AddOrkeonPostgresCheckpointing(Config(("ConnectionString", "Host=localhost;Database=none"))),
        })
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
            if (memoryFirst)
            {
                services.AddOrkeonCheckpointing();
                register(services);
            }
            else
            {
                register(services);
                services.AddOrkeonCheckpointing();
            }

            using var sp = services.BuildServiceProvider();
            var store = sp.GetRequiredService<IStateStore>();

            Assert.IsNotType<InMemoryStateStore>(store);
            Assert.NotNull(sp.GetRequiredService<ICheckpointManager>());
        }
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldChooseTheJsonStore_WhenStoreIsJson()
    {
        using var sp = Provider(Config(("Enabled", "true"), ("Store", "json")), memoryFirst: true);

        Assert.IsType<JsonFileStateStore>(sp.GetRequiredService<IStateStore>());
    }

    [Theory]
    [InlineData("false", "sqlite")]
    [InlineData("true", "memory")]
    [Trait("BR", "FWK-01")]
    public void ShouldKeepTheInMemoryStore_WhenDisabledOrMemory(string enabled, string store)
    {
        using var sp = Provider(Config(("Enabled", enabled), ("Store", store)), memoryFirst: true);

        Assert.IsType<InMemoryStateStore>(sp.GetRequiredService<IStateStore>());
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldRefuseAnUnknownStore()
    {
        using var sp = Provider(Config(("Enabled", "true"), ("Store", "mongo")), memoryFirst: true);

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IStateStore>());
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldRefusePostgresWithoutAConnectionString()
    {
        using var sp = Provider(Config(("Enabled", "true"), ("Store", "postgres")), memoryFirst: true);

        var error = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<CheckpointingOptions>>().Value);
        Assert.Contains("ConnectionString", error.Message, StringComparison.Ordinal);
    }
}
