using System.Data.Common;
using Orkeon.Tools.Abstractions.Data;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// ADO.NET provider factory that resolves <see cref="DbProviderFactory"/> instances by provider name.
/// Supports SQL Server, PostgreSQL, MySQL/MariaDB, and SQLite out of the box.
/// </summary>
public sealed class DatabaseProviderFactory : IDatabaseProviderFactory
{
    private static readonly Dictionary<string, Func<DbProviderFactory>> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Data.SqlClient"] = () => Microsoft.Data.SqlClient.SqlClientFactory.Instance,
        ["SqlServer"] = () => Microsoft.Data.SqlClient.SqlClientFactory.Instance,
        ["Npgsql"] = () => Npgsql.NpgsqlFactory.Instance,
        ["PostgreSQL"] = () => Npgsql.NpgsqlFactory.Instance,
        ["MySqlConnector"] = () => MySqlConnector.MySqlConnectorFactory.Instance,
        ["MySQL"] = () => MySqlConnector.MySqlConnectorFactory.Instance,
        ["Microsoft.Data.Sqlite"] = () => Microsoft.Data.Sqlite.SqliteFactory.Instance,
        ["SQLite"] = () => Microsoft.Data.Sqlite.SqliteFactory.Instance,
    };

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedProviders { get; } =
        ["Microsoft.Data.SqlClient", "Npgsql", "MySqlConnector", "Microsoft.Data.Sqlite"];

    /// <inheritdoc />
    public DbProviderFactory GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name cannot be empty.", nameof(providerName));

        if (Providers.TryGetValue(providerName, out var factory))
            return factory();

        throw new NotSupportedException(
            $"Database provider '{providerName}' is not supported. Supported providers: {string.Join(", ", SupportedProviders)}");
    }

    /// <inheritdoc />
    public bool IsSupported(string providerName)
        => !string.IsNullOrWhiteSpace(providerName) && Providers.ContainsKey(providerName);
}
