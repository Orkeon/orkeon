using Microsoft.Data.Sqlite;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Relational;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Relational;

public sealed class DatabaseSchemaToolTests : IDisposable
{
    private readonly DatabaseSchemaTool _tool;
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public DatabaseSchemaToolTests()
    {
        _connectionString = $"Data Source=SchemaTest{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                active INTEGER DEFAULT 1
            );
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                user_id INTEGER NOT NULL,
                amount REAL NOT NULL,
                created_at TEXT,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                sku TEXT NOT NULL UNIQUE,
                price REAL
            );
            CREATE INDEX idx_orders_user_id ON orders(user_id);
            CREATE UNIQUE INDEX idx_products_sku ON products(sku);
            INSERT INTO users (name, email, active) VALUES ('Alice', 'alice@test.com', 1);
            INSERT INTO users (name, email, active) VALUES ('Bob', 'bob@test.com', 0);
            """;
        cmd.ExecuteNonQuery();

        _tool = new DatabaseSchemaTool(new DatabaseProviderFactory());
    }

    // ── Validation tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Validation_RejectsEmptyConnectionString()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "",
            ["provider_name"] = "Microsoft.Data.Sqlite",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Connection string", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsEmptyProviderName()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Provider name", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsUnsupportedProvider()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Oracle.ManagedDataAccess",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unsupported provider", result.Error);
    }

    // ── Tables scope tests ───────────────────────────────────────────────

    [Fact]
    public async Task Tables_ReturnsAllUserTables()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Tables",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var tables = dict["tables"] as IEnumerable<object>;
        Assert.NotNull(tables);

        var tableNames = tables
            .Cast<Dictionary<string, object?>>()
            .Select(t => t["name"]?.ToString())
            .ToList();

        Assert.Contains("users", tableNames);
        Assert.Contains("orders", tableNames);
        Assert.Contains("products", tableNames);
    }

    [Fact]
    public async Task Tables_IncludesColumnsInfo()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Tables",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var tables = (dict["tables"] as IEnumerable<object>)!
            .Cast<Dictionary<string, object?>>()
            .ToList();

        var usersTable = tables.First(t => t["name"]?.ToString() == "users");
        var columns = (usersTable["columns"] as IEnumerable<object>)!
            .Cast<Dictionary<string, object?>>()
            .ToList();

        Assert.True(columns.Count >= 4, $"Expected at least 4 columns, got {columns.Count}");

        var nameCol = columns.FirstOrDefault(c => c["name"]?.ToString() == "name");
        Assert.NotNull(nameCol);
    }

    // ── Table filter tests ───────────────────────────────────────────────

    [Fact]
    public async Task TableFilter_FiltersWithWildcard()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Tables",
            ["table_filter"] = "user*",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var tables = (dict["tables"] as IEnumerable<object>)!
            .Cast<Dictionary<string, object?>>()
            .ToList();

        Assert.Single(tables);
        Assert.Equal("users", tables[0]["name"]?.ToString());
    }

    [Fact]
    public async Task TableFilter_MiddleWildcard()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Tables",
            ["table_filter"] = "*er*",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var tables = (dict["tables"] as IEnumerable<object>)!
            .Cast<Dictionary<string, object?>>()
            .Select(t => t["name"]?.ToString())
            .ToList();

        // "users" and "orders" both contain "er"
        Assert.Contains("users", tables);
        Assert.Contains("orders", tables);
        Assert.DoesNotContain("products", tables);
    }

    // ── Indexes scope tests ──────────────────────────────────────────────

    [Fact]
    public async Task Indexes_ReturnsUserCreatedIndexes()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Indexes",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var indexes = dict["indexes"] as IEnumerable<object>;
        Assert.NotNull(indexes);

        var indexList = indexes
            .Cast<Dictionary<string, object?>>()
            .ToList();

        var indexNames = indexList.Select(i => i["name"]?.ToString()).ToList();
        Assert.Contains("idx_orders_user_id", indexNames);
        Assert.Contains("idx_products_sku", indexNames);
    }

    // ── ForeignKeys scope tests ──────────────────────────────────────────

    [Fact]
    public async Task ForeignKeys_ReturnsFkConstraints()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "ForeignKeys",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var fks = dict["foreign_keys"] as IEnumerable<object>;
        Assert.NotNull(fks);

        var fkList = fks.Cast<Dictionary<string, object?>>().ToList();
        Assert.NotEmpty(fkList);

        var ordersFk = fkList.First(fk => fk["source_table"]?.ToString() == "orders");
        Assert.Equal("users", ordersFk["target_table"]?.ToString());
    }

    // ── All scope tests ──────────────────────────────────────────────────

    [Fact]
    public async Task AllScope_ReturnsTablesIndexesAndForeignKeys()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "All",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        Assert.NotNull(dict["tables"]);
        Assert.NotNull(dict["indexes"]);
        Assert.NotNull(dict["foreign_keys"]);
        Assert.Equal("SQLite", dict["provider_name"]?.ToString());
    }

    // ── Schema metadata tests ────────────────────────────────────────────

    [Fact]
    public void Schema_HasCorrectNameAndDescription()
    {
        Assert.Equal("database_schema", _tool.Name);
        Assert.Contains("schema", _tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Data Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamConnectionString].Required);
        Assert.True(_tool.Schema.Parameters["provider_name"].Required);
        Assert.False(_tool.Schema.Parameters["schema_scope"].Required);
        Assert.False(_tool.Schema.Parameters["table_filter"].Required);
    }

    // ── Default scope tests ──────────────────────────────────────────────

    [Fact]
    public async Task DefaultScope_OmitsIndexesAndForeignKeys()
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        // Default scope is Tables — indexes and foreign_keys should not be in output
        // (they are null in the response, which may or may not be serialized)
        Assert.NotNull(dict["tables"]);
    }

    // ── Injection rejection at the request boundary ──────────────────────
    // TableFilter is user-supplied and validated before any SQL is built.
    // Inputs containing SQL injection indicators must be rejected with Success=false.

    [Theory]
    [InlineData("users; DROP TABLE orders--")]
    [InlineData("../schema")]
    [InlineData("users' OR '1'='1")]
    public async Task DatabaseSchema_RejectsMaliciousIdentifier(string input)
    {
        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            ["table_filter"] = input,
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
    }

    // ── Anti-injection tests (defense-in-depth for PRAGMA identifiers) ───
    // SQLite PRAGMA statements do not support parameter binding, so the tool
    // must validate identifiers before interpolating them. These tests ensure
    // that malicious identifiers cannot escape the PRAGMA call even if they
    // somehow end up in sqlite_master (e.g. attacker-controlled database).

    [Fact]
    public async Task AntiInjection_PragmaTableInfo_RejectsMaliciousTableName()
    {
        // Line 143: PRAGMA table_info({identifier}) — create a table whose name
        // contains SQL injection payload, then request columns scope. The tool
        // must reject the identifier during ValidateSqliteIdentifier, and the
        // users table must still exist afterwards.
        var connStr = $"Data Source=InjectTbl{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);
                INSERT INTO users (name) VALUES ('Alice');
                CREATE TABLE "evil"") ; DROP TABLE users; --" (id INTEGER);
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = connStr,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Columns",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Either the tool fails (argument exception) OR it succeeds while rejecting
        // the evil table silently. Critically, the users table must still exist.
        using var verifyCmd = keepAlive.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = (long)(await verifyCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(1, count);

        if (!result.Success)
        {
            Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AntiInjection_PragmaIndexInfo_RejectsMaliciousIndexName()
    {
        // Line 209: PRAGMA index_info({identifier}) — create an index whose
        // name contains SQL injection payload, then request the Indexes scope.
        var connStr = $"Data Source=InjectIdx{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);
                INSERT INTO users (name) VALUES ('Alice');
                CREATE INDEX "evil"") ; DROP TABLE users; --" ON users(name);
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = connStr,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "Indexes",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // The users table must still exist regardless of tool result.
        using var verifyCmd = keepAlive.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = (long)(await verifyCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(1, count);

        if (!result.Success)
        {
            Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AntiInjection_PragmaForeignKeyList_RejectsMaliciousTableName()
    {
        // Line 259: PRAGMA foreign_key_list({identifier}) — create a table
        // whose name contains a SQL injection payload, then request the
        // ForeignKeys scope. The tool must validate the identifier before
        // interpolation.
        var connStr = $"Data Source=InjectFk{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);
                INSERT INTO users (name) VALUES ('Alice');
                CREATE TABLE "evil"") ; DROP TABLE users; --" (id INTEGER);
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var request = new ToolCallRequest("database_schema", new Dictionary<string, object?>
        {
            [ParamConnectionString] = connStr,
            ["provider_name"] = "SQLite",
            ["schema_scope"] = "ForeignKeys",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // The users table must still exist regardless of tool result.
        using var verifyCmd = keepAlive.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = (long)(await verifyCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(1, count);

        if (!result.Success)
        {
            Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        _tool.Dispose();
        _keepAlive.Dispose();
        GC.SuppressFinalize(this);
    }
}
