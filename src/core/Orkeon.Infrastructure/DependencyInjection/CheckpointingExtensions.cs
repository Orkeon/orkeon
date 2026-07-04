using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>
    /// Adds session checkpointing services with a PostgreSQL state store.
    /// Reads the connection string from configuration key "Orkeon:Checkpointing:ConnectionString".
    /// </summary>
    public static IServiceCollection AddOrkeonPostgresCheckpointing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresStateStoreOptions>(opts =>
        {
            opts.ConnectionString = configuration["Orkeon:Checkpointing:ConnectionString"] ?? "";
            opts.SchemaName = configuration["Orkeon:Checkpointing:SchemaName"] ?? "orkeon";

            if (bool.TryParse(configuration["Orkeon:Checkpointing:AutoMigrate"], out var autoMigrate))
                opts.AutoMigrate = autoMigrate;

            if (int.TryParse(configuration["Orkeon:Checkpointing:MaxHistoryPerSession"], out var maxHistory))
                opts.MaxHistoryPerSession = maxHistory;
        });

        services.TryAddSingleton<IStateStore, PostgresStateStore>();
        services.TryAddSingleton<ICheckpointManager, CheckpointManager>();
        services.TryAddSingleton<IResumeEngine, ResumeEngine>();
        return services;
    }
}
