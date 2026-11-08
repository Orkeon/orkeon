using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    /// Adds session checkpointing services whose store the <c>Orkeon:Checkpointing</c> section chooses
    /// (<see cref="CheckpointingOptions"/>): <c>Enabled</c> with <c>Store</c> memory, json, sqlite or
    /// postgres. With <c>Enabled</c> false, or the memory store, nothing outlives the process. The
    /// section is bound and judged when the host starts; the store is chosen when it is first
    /// resolved, so the order of the registrations does not matter. Only <see cref="IStateStore"/> is
    /// replaced: <see cref="ICheckpointManager"/> and <see cref="IResumeEngine"/> stay
    /// <c>TryAdd</c>, their implementation does not depend on the store.
    /// </summary>
    public static IServiceCollection AddOrkeonCheckpointing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CheckpointingOptions>()
            .Bind(configuration.GetSection(CheckpointingSection))
            .ValidateSettings((o, _) => ProblemsOf(o))
            .DeclareSettings(CheckpointingSection);

        services.AddOrkeonCheckpointing();
        services.Replace(ServiceDescriptor.Singleton<IStateStore>(sp =>
            CreateConfiguredStore(sp.GetRequiredService<IOptions<CheckpointingOptions>>().Value, sp)));
        return services;
    }

    private static IEnumerable<string> ProblemsOf(CheckpointingOptions o)
    {
        if (!o.Enabled)
            yield break;

        if (!Enum.IsDefined(o.Store))
            yield return $"{CheckpointingSection}:Store is {o.Store}; accepted: memory, json, sqlite, postgres.";

        if (o.Store == CheckpointStore.Postgres && string.IsNullOrWhiteSpace(o.ConnectionString))
            yield return $"{CheckpointingSection}:ConnectionString is required when Store is postgres.";

        if (o.Path is { } path && !path.StartsWith('/'))
            yield return $"{CheckpointingSection}:Path is '{path}'; it is a virtual path, under {CheckpointingOptions.StateRoot} (it starts with '/').";
    }

    private static IStateStore CreateConfiguredStore(CheckpointingOptions o, IServiceProvider sp)
    {
        if (!o.Enabled)
            return new InMemoryStateStore();

        // The store is infrastructure: it writes under /state, an internal mount no agent addresses, so it
        // takes the privileged view of the file system (ADR-008), like the LLM exchange log.
        var fs = sp.GetService<Orkeon.Infrastructure.FileSystem.PrivilegedFileSystemAccess>()?.FileSystem
            ?? sp.GetRequiredService<Orkeon.Domain.FileSystem.IFileSystemService>();
        return o.Store switch
        {
            CheckpointStore.Memory => new InMemoryStateStore(),
            CheckpointStore.Json => new JsonFileStateStore(fs, o.Path ?? $"{CheckpointingOptions.StateRoot}/checkpoints", o.MaxVersions),
            CheckpointStore.Sqlite => new SqliteStateStore(
                string.IsNullOrWhiteSpace(o.ConnectionString)
                    ? $"Data Source={o.Path ?? $"{CheckpointingOptions.StateRoot}/checkpoints.db"}"
                    : o.ConnectionString,
                fs),
            CheckpointStore.Postgres => new PostgresStateStore(Options.Create(new PostgresStateStoreOptions
            {
                ConnectionString = o.ConnectionString ?? "",
                SchemaName = o.SchemaName,
                AutoMigrate = o.AutoMigrate,
                MaxHistoryPerSession = o.MaxHistoryPerSession,
            })),
            _ => throw new InvalidOperationException($"{CheckpointingSection}:Store {o.Store} is not a checkpoint store."),
        };
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

        // Replace, not TryAdd: AddOrkeonInfrastructure has already registered the in-memory store,
        // and a TryAdd after it would leave the durable store unused. Only IStateStore is replaced:
        // the manager and the engine do not depend on which store backs them.
        services.Replace(ServiceDescriptor.Singleton<IStateStore>(sp => new SqliteStateStore(
            connectionString,
            sp.GetRequiredService<Orkeon.Domain.FileSystem.IFileSystemService>())));
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

        services.Replace(ServiceDescriptor.Singleton<IStateStore, PostgresStateStore>());
        services.TryAddSingleton<ICheckpointManager, CheckpointManager>();
        services.TryAddSingleton<IResumeEngine, ResumeEngine>();
        return services;
    }
}
