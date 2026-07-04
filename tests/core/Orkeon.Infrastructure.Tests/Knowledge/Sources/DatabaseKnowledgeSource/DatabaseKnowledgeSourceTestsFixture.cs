using Microsoft.Data.Sqlite;
using Orkeon.Infrastructure.Knowledge.Sources;

namespace Orkeon.Infrastructure.Tests.Knowledge.Sources;

public sealed class DatabaseKnowledgeSourceTestsFixture : IDisposable
{
    private readonly List<SqliteConnection> _connections = [];

    /// <summary>
    /// Creates an in-memory SQLite connection that stays open for the lifetime of the fixture.
    /// The connection is returned open so the caller can set up tables and insert test data.
    /// </summary>
    public SqliteConnection CreateInMemoryConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// Creates a table and populates it with test data using the given open connection.
    /// Returns the connection string that the <see cref="DatabaseKnowledgeSource"/> should use.
    /// Because SQLite in-memory databases are tied to a connection, we use a shared cache
    /// so multiple connections can access the same database.
    /// </summary>
    public string SetupDatabase(
        out SqliteConnection keepAlive,
        (string id, string title, string content)[]? rows = null,
        bool includeNulls = false)
    {
        // Use a shared-cache, named in-memory database so that the source's own connection
        // can see the tables created here.
        var dbName = $"test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        _connections.Add(keepAlive);

        using var cmd = keepAlive.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Documents (
                Id TEXT,
                Title TEXT,
                Content TEXT
            );";
        cmd.ExecuteNonQuery();

        if (rows != null)
        {
            foreach (var (id, title, content) in rows)
            {
                using var insertCmd = keepAlive.CreateCommand();
                insertCmd.CommandText = "INSERT INTO Documents (Id, Title, Content) VALUES (@id, @title, @content)";
                insertCmd.Parameters.AddWithValue("@id", (object?)id ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@content", (object?)content ?? DBNull.Value);
                insertCmd.ExecuteNonQuery();
            }
        }

        if (includeNulls)
        {
            using var nullCmd = keepAlive.CreateCommand();
            nullCmd.CommandText = "INSERT INTO Documents (Id, Title, Content) VALUES (NULL, NULL, NULL)";
            nullCmd.ExecuteNonQuery();
        }

        return connectionString;
    }

    public static DatabaseKnowledgeSource CreateSource(DatabaseKnowledgeSourceOptions options)
        => new(options, SqliteFactory.Instance);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var conn in _connections)
        {
            try { conn.Close(); conn.Dispose(); } catch { }
        }
    }
}
