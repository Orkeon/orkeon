using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Tools.Data.Tests.Doubles;

/// <summary>
/// A <see cref="DbConnection"/> that answers <see cref="GetSchema(string, string?[])"/> from
/// configured tables, so the generic ADO.NET introspection path runs without a server.
/// Its type name does not contain "Sqlite", so tools take their non-SQLite branch.
/// </summary>
public sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    /// <summary>Schema answers by collection name; the restrictions passed are handed to the function.</summary>
    public Dictionary<string, Func<string?[], DataTable>> Schemas { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Collections whose lookup throws, to exercise the best-effort fault barriers.</summary>
    public HashSet<string> FailingCollections { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every schema lookup, in order: collection name and restrictions.</summary>
    public List<(string Collection, string?[] Restrictions)> SchemaCalls { get; } = [];

    /// <summary>Value returned by <see cref="Database"/>.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Number of calls to <see cref="Open"/>.</summary>
    public int OpenCount { get; private set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => DatabaseName;

    public override string DataSource => "fake";

    public override string ServerVersion => "1.0";

    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) => DatabaseName = databaseName;

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open()
    {
        OpenCount++;
        _state = ConnectionState.Open;
    }

    public override DataTable GetSchema(string collectionName) => GetSchema(collectionName, []);

    public override DataTable GetSchema(string collectionName, string?[] restrictionValues)
    {
        SchemaCalls.Add((collectionName, restrictionValues));

        if (FailingCollections.Contains(collectionName))
            throw new NotSupportedException($"Collection '{collectionName}' is not supported by the fake.");

        // Like real providers, an unknown collection is an argument error.
        if (!Schemas.TryGetValue(collectionName, out var answer))
            throw new ArgumentException($"The requested collection '{collectionName}' is not defined.", nameof(collectionName));

        return answer(restrictionValues);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException("FakeDbConnection has no transaction.");

    protected override DbCommand CreateDbCommand() =>
        throw new NotSupportedException("FakeDbConnection has no command.");

    /// <summary>Builds a table of <see cref="object"/> columns from a header and rows.</summary>
    public static DataTable Table(string[] columns, params object?[][] rows)
    {
        var table = new DataTable();
        foreach (var column in columns)
            table.Columns.Add(column, typeof(object));

        foreach (var row in rows)
            table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());

        return table;
    }
}
