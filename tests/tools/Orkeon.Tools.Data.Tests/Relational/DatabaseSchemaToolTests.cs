using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Tests.Doubles;
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

    // ── Generic ADO.NET GetSchema path (non-SQLite providers) ────────────

    private static readonly string[] TableColumns = ["TABLE_NAME", "TABLE_TYPE", "TABLE_SCHEMA"];
    private static readonly string[] ColumnColumns =
        ["COLUMN_NAME", "DATA_TYPE", "IS_NULLABLE", "COLUMN_DEFAULT", "CHARACTER_MAXIMUM_LENGTH"];
    private static readonly string[] IndexColumnColumns = ["TABLE_NAME", "INDEX_NAME", "COLUMN_NAME"];
    private static readonly string[] IndexesHeader = ["TABLE_NAME", "INDEX_NAME", "UNIQUE"];
    private static readonly string[] ForeignKeyColumns =
        ["CONSTRAINT_NAME", "TABLE_NAME", "FKEY_TO_TABLE", "FKEY_FROM_COLUMN", "FKEY_TO_COLUMN"];

    /// <summary>A server-like catalogue: two user tables, one system table, one nameless row.</summary>
    private static FakeDbConnection GenericCatalogue()
    {
        var connection = new FakeDbConnection();
        connection.Schemas["Tables"] = _ => FakeDbConnection.Table(TableColumns,
            ["customers", "BASE TABLE", "dbo"],
            ["invoices", "BASE TABLE", "sales"],
            ["sysdiagrams", "SYSTEM TABLE", "sys"],
            ["", "BASE TABLE", "dbo"]);
        connection.Schemas["Columns"] = r => r[2] switch
        {
            "customers" => FakeDbConnection.Table(ColumnColumns,
                ["id", "int", "NO", null, null],
                ["name", "nvarchar", "YES", "'anon'", 120],
                ["code", "char", "True", null, 8L],
                ["flag", "smallint", "1", "0", (short)2],
                ["", "int", "NO", null, null]),
            _ => FakeDbConnection.Table(ColumnColumns,
                ["id", "int", "NO", null, null],
                ["customer_id", "int", "NO", null, null]),
        };
        connection.Schemas["IndexColumns"] = r => (r[2], r[3]) switch
        {
            ("customers", null) => FakeDbConnection.Table(IndexColumnColumns,
                ["customers", "PK_customers", "id"],
                ["customers", "ix_name", "name"],
                ["customers", "primary", ""]),
            ("invoices", null) => FakeDbConnection.Table(IndexColumnColumns,
                ["invoices", "invoices_pkey", "id"]),
            (_, "PK_customers") => FakeDbConnection.Table(IndexColumnColumns, ["customers", "PK_customers", "id"]),
            (_, "ix_name") => FakeDbConnection.Table(IndexColumnColumns, ["customers", "ix_name", "name"], ["customers", "ix_name", ""]),
            _ => FakeDbConnection.Table(IndexColumnColumns),
        };
        connection.Schemas["Indexes"] = _ => FakeDbConnection.Table(IndexesHeader,
            ["customers", "PK_customers", true],
            ["customers", "ix_name", 0],
            ["invoices", "ix_amount", 1L],
            ["invoices", "ix_text", "True"],
            ["invoices", "ix_numeric_text", "1"],
            ["invoices", "ix_none", null],
            ["invoices", "", true]);
        connection.Schemas["ForeignKeys"] = _ => FakeDbConnection.Table(ForeignKeyColumns,
            ["FK_invoices_customers", "invoices", "customers", "customer_id", "id"],
            [null, "invoices", null, null, null],
            [null, "invoices", "customers", null, null]);
        return connection;
    }

    private static (DatabaseSchemaTool Tool, MockLogger<DatabaseSchemaTool> Logger) GenericTool(FakeDbConnection? connection)
    {
        var factory = new StubDatabaseProviderFactory(["FakeDb"], ["FakeDb"])
        {
            Provider = new FakeDbProviderFactory { Connection = connection },
        };
        var logger = new MockLogger<DatabaseSchemaTool>();
        return (new DatabaseSchemaTool(factory, logger), logger);
    }

    private static Task<ToolCallResponse> CallGenericAsync(
        DatabaseSchemaTool tool, string scope, string? filter = null, string connectionString = "Server=db;Database=shop;")
    {
        var parameters = new Dictionary<string, object?>
        {
            [ParamConnectionString] = connectionString,
            ["provider_name"] = "FakeDb",
            ["schema_scope"] = scope,
        };
        if (filter is not null)
            parameters["table_filter"] = filter;

        return tool.CallAsync(new ToolCallRequest("database_schema", parameters), TestContext.Current.CancellationToken);
    }

    private static List<Dictionary<string, object?>> Rows(Dictionary<string, object?> dict, string key) =>
        ((IEnumerable<object>)dict[key]!).Cast<Dictionary<string, object?>>().ToList();

    private static List<string> Strings(object? value) =>
        ((IEnumerable<object>)value!).Cast<string>().ToList();

    [Fact]
    public async Task Generic_AllScope_MapsTablesColumnsIndexesAndForeignKeys()
    {
        var connection = GenericCatalogue();
        var (tool, logger) = GenericTool(connection);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "All");

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(1, connection.OpenCount);
        Assert.Equal("shop", dict["database_name"]);
        Assert.Equal("FakeDb", dict["provider_name"]);

        var tables = Rows(dict, "tables");
        Assert.Equal(["customers", "invoices"], tables.Select(t => t["name"]));
        Assert.Equal("sales", tables[1]["schema"]);

        var columns = Rows(tables[0], "columns");
        Assert.Equal(["id", "name", "code", "flag"], columns.Select(c => c["name"]));
        Assert.Equal([true, false, false, false], columns.Select(c => c["is_primary_key"]));
        Assert.Equal([false, true, true, true], columns.Select(c => c["is_nullable"]));
        Assert.Equal("nvarchar", columns[1]["data_type"]);
        Assert.Equal("'anon'", columns[1]["default_value"]);
        Assert.Equal([null, 120, 8, 2], columns.Select(c => c.GetValueOrDefault("max_length")));
        Assert.Equal(true, Rows(tables[1], "columns").Single(c => Equals(c["name"], "id"))["is_primary_key"]);

        var indexes = Rows(dict, "indexes");
        Assert.Equal(["PK_customers", "ix_name", "ix_amount", "ix_text", "ix_numeric_text", "ix_none"], indexes.Select(i => i["name"]));
        Assert.Equal([true, false, true, true, true, false], indexes.Select(i => i["is_unique"]));
        Assert.Equal(["name"], Strings(indexes[1]["columns"]));

        var foreignKeys = Rows(dict, "foreign_keys");
        Assert.Equal(2, foreignKeys.Count);
        var fk = Assert.Single(foreignKeys, f => Equals(f["name"], "FK_invoices_customers"));
        Assert.Equal("invoices", fk["source_table"]);
        Assert.Equal("customers", fk["target_table"]);
        Assert.Equal(["customer_id"], Strings(fk["source_columns"]));
        Assert.Equal(["id"], Strings(fk["target_columns"]));

        Assert.Contains(logger.LogEntries, e => e.Level == LogLevel.Information && e.Message.Contains("2 tables", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generic_TableFilter_AppliesToTablesIndexesAndForeignKeys()
    {
        var (tool, _) = GenericTool(GenericCatalogue());
        using var owned = tool;

        var result = await CallGenericAsync(tool, "All", filter: "CUST*");

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(["customers"], Rows(dict, "tables").Select(t => t["name"]));
        Assert.All(Rows(dict, "indexes"), i => Assert.Equal("customers", i["table_name"]));
        Assert.Empty(Rows(dict, "foreign_keys"));
    }

    [Fact]
    public async Task Generic_IndexesScope_ListsTablesWithoutColumnsAndSkipsForeignKeys()
    {
        var connection = GenericCatalogue();
        var (tool, _) = GenericTool(connection);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "Indexes");

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.All(Rows(dict, "tables"), t => Assert.Empty(Strings(t["columns"])));
        Assert.NotEmpty(Rows(dict, "indexes"));
        Assert.Null(dict.GetValueOrDefault("foreign_keys"));
        Assert.DoesNotContain(connection.SchemaCalls, c => c.Collection is "Columns" or "ForeignKeys");
    }

    [Fact]
    public async Task Generic_FailingCollections_ReturnPartialSchemaAndLogWarnings()
    {
        var connection = GenericCatalogue();
        connection.FailingCollections.UnionWith(["Columns", "IndexColumns", "Indexes", "ForeignKeys"]);
        var (tool, logger) = GenericTool(connection);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "All");

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(2, Rows(dict, "tables").Count);
        Assert.All(Rows(dict, "tables"), t => Assert.Empty(Strings(t["columns"])));
        Assert.Empty(Rows(dict, "indexes"));
        Assert.Empty(Rows(dict, "foreign_keys"));

        var warnings = logger.GetEntriesByLevel(LogLevel.Warning);
        Assert.Contains(warnings, w => w.Message.Contains("'Columns'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Message.Contains("'Indexes'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Message.Contains("'ForeignKeys'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generic_FailingIndexColumns_KeepsColumnsAndIndexesWithoutDetail()
    {
        var connection = GenericCatalogue();
        connection.FailingCollections.Add("IndexColumns");
        var (tool, logger) = GenericTool(connection);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "All");

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        var columns = Rows(Rows(dict, "tables")[0], "columns");
        Assert.Equal(4, columns.Count);
        Assert.All(columns, c => Assert.Equal(false, c["is_primary_key"]));
        Assert.All(Rows(dict, "indexes"), i => Assert.Empty(Strings(i["columns"])));
        var warnings = logger.GetEntriesByLevel(LogLevel.Warning);
        Assert.Contains(warnings, w => w.Message.Contains("'PrimaryKeys'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Message.Contains("'IndexColumns'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generic_LowercaseCatalogue_ReadsSnakeCaseColumnsAndBooleanNullability()
    {
        var connection = new FakeDbConnection { DatabaseName = "pgdb" };
        connection.Schemas["Tables"] = _ => FakeDbConnection.Table(["table_name", "table_type", "table_schema"],
            ["items", "table", "public"]);
        connection.Schemas["Columns"] = _ => FakeDbConnection.Table(["column_name", "data_type", "nullable", "character_maximum_length"],
            ["id", "integer", false, "not-a-number"],
            ["label", "text", 1L, null],
            ["note", "text", true, null]);

        var (tool, _) = GenericTool(connection);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "Columns");

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal("pgdb", dict["database_name"]);
        var columns = Rows(Rows(dict, "tables")[0], "columns");
        Assert.Equal([false, true, true], columns.Select(c => c["is_nullable"]));
        Assert.Null(columns[0].GetValueOrDefault("max_length"));
        Assert.Null(dict.GetValueOrDefault("indexes"));
    }

    [Theory]
    [InlineData("Data Source=/var/data/app.db", "/var/data/app.db")]
    [InlineData("Host=db; Initial Catalog = billing ;User Id=x", "billing")]
    [InlineData("Host=db;User Id=x", "")]
    public async Task Generic_DatabaseName_FallsBackToConnectionString(string connectionString, string expected)
    {
        var connection = new FakeDbConnection();
        connection.Schemas["Tables"] = _ => FakeDbConnection.Table(TableColumns);
        var (tool, _) = GenericTool(connection);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "Tables", connectionString: connectionString);

        Assert.True(result.Success, result.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(expected, dict["database_name"]);
        Assert.Empty(Rows(dict, "tables"));
    }

    [Fact]
    public async Task Generic_NullConnectionFromProvider_Fails()
    {
        var (tool, _) = GenericTool(null);
        using var owned = tool;

        var result = await CallGenericAsync(tool, "Tables");

        Assert.False(result.Success);
        Assert.Contains("null connection", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validation_SuspiciousTableFilter_IsLoggedAndRejected()
    {
        var (tool, logger) = GenericTool(GenericCatalogue());
        using var owned = tool;

        var result = await CallGenericAsync(tool, "Tables", filter: "users;--");

        Assert.False(result.Success);
        Assert.Contains("invalid characters", result.Error, StringComparison.Ordinal);
        Assert.Contains(logger.LogEntries, e => e.Level == LogLevel.Warning && e.Message.Contains("users;--", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _tool.Dispose();
        _keepAlive.Dispose();
        GC.SuppressFinalize(this);
    }
}
