using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Services.Checkpointing;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// Extension methods for registering session checkpointing services.
/// </summary>
public static class CheckpointingExtensions
{
    /// <summary>
    /// Adds session checkpointing services with an in-memory state store.
    /// </summary>
    public static IServiceCollection AddOrkeonCheckpointing(this IServiceCollection services)
    {
        services.TryAddSingleton<IStateStore, InMemoryStateStore>();
        services.TryAddSingleton<ICheckpointManager, CheckpointManager>();
        services.TryAddSingleton<IResumeEngine, ResumeEngine>();
        return services;
    }

    /// <summary>
    /// Adds session checkpointing services with a SQLite state store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// SQLite connection string (e.g., <c>"Data Source=checkpoints.db"</c> or
    /// <c>"Data Source=:memory:"</c>).
    /// </param>
    public static IServiceCollection AddOrkeonSqliteCheckpointing(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IStateStore>(sp => new SqliteStateStore(
            connectionString,
            sp.GetRequiredService<Orkeon.Domain.FileSystem.IFileSystemService>()));
        services.TryAddSingleton<ICheckpointManager, CheckpointManager>();
        services.TryAddSingleton<IResumeEngine, ResumeEngine>();
        return services;
    }

    /// <summary>The section bound to <see cref="PostgresStateStoreOptions"/>.</summary>
    private const string CheckpointingSection = "Orkeon:Checkpointing";

    /// <summary>
    /// Adds session checkpointing services with a PostgreSQL state store, configured by the
    /// <c>Orkeon:Checkpointing</c> section (<c>ConnectionString</c>, <c>SchemaName</c>,
    /// <c>AutoMigrate</c>, <c>MaxHistoryPerSession</c>) — bound, and judged when the host starts
    /// (GAP-40): an <c>AutoMigrate</c> or a <c>MaxHistoryPerSession</c> it could not read kept its
    /// default without a word.
    /// </summary>
    public static IServiceCollection AddOrkeonPostgresCheckpointing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<PostgresStateStoreOptions>()
            .Bind(configuration.GetSection(CheckpointingSection))
            .DeclareSettings(CheckpointingSection);

        services.TryAddSingleton<IStateStore, PostgresStateStore>();
        services.TryAddSingleton<ICheckpointManager, CheckpointManager>();
        services.TryAddSingleton<IResumeEngine, ResumeEngine>();
        return services;
    }
}
