using Orkeon.Tools.Abstractions.Data;
using Orkeon.Tools.Data.Relational;

namespace Orkeon.Tools.Data.Tests.Relational;

public class DatabaseSecurityPolicyTests
{
    private readonly DefaultDatabaseSecurityPolicy _policy = new();
    private readonly DatabaseQueryOptions _defaultOptions = new();

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("drop table users")]
    [InlineData("TRUNCATE TABLE orders")]
    [InlineData("CREATE TABLE test (id INT)")]
    [InlineData("ALTER TABLE users ADD COLUMN age INT")]
    [InlineData("GRANT SELECT ON users TO public")]
    [InlineData("REVOKE ALL ON users FROM public")]
    public void ValidateQuery_BlocksDdl_WhenNotAllowed(string query)
    {
        var result = _policy.ValidateQuery(query, _defaultOptions);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("TRUNCATE TABLE orders")]
    [InlineData("ALTER TABLE users DROP COLUMN age")]
    public void ValidateQuery_AllowsDdl_WhenExplicitlyAllowed(string query)
    {
        var options = new DatabaseQueryOptions { AllowDdl = true, AllowDestructiveDeletes = true };

        var result = _policy.ValidateQuery(query, options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_BlocksDeleteWithoutWhere()
    {
        var result = _policy.ValidateQuery("DELETE FROM users", _defaultOptions);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_AllowsDeleteWithWhere()
    {
        var result = _policy.ValidateQuery("DELETE FROM users WHERE id = 1", _defaultOptions);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_AllowsDestructiveDelete_WhenExplicitlyAllowed()
    {
        var options = new DatabaseQueryOptions { AllowDestructiveDeletes = true };

        var result = _policy.ValidateQuery("DELETE FROM users", options);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT name FROM users WHERE id = 1")]
    [InlineData("INSERT INTO users (name) VALUES ('test')")]
    [InlineData("UPDATE users SET name = 'test' WHERE id = 1")]
    [InlineData("DELETE FROM users WHERE id = 1")]
    public void ValidateQuery_AllowsSafeQueries(string query)
    {
        var result = _policy.ValidateQuery(query, _defaultOptions);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_RejectsEmptyQuery()
    {
        var result = _policy.ValidateQuery("", _defaultOptions);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("DROP TABLE users", true)]
    [InlineData("TRUNCATE TABLE orders", true)]
    [InlineData("DELETE FROM users", true)]
    [InlineData("SELECT * FROM users", false)]
    [InlineData("DELETE FROM users WHERE id = 1", false)]
    [InlineData("INSERT INTO users (name) VALUES ('test')", false)]
    public void IsDestructiveOperation_DetectsCorrectly(string query, bool expected)
    {
        var result = _policy.IsDestructiveOperation(query);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ValidateQuery_BlocksAlterTableDrop()
    {
        var result = _policy.ValidateQuery("ALTER TABLE users DROP COLUMN age", _defaultOptions);

        Assert.False(result.IsValid);
    }

    // ── R2.4: stacked statements, comment-prefix, UPDATE/DELETE without WHERE ──

    [Fact]
    public void ValidateQuery_BlocksStackedDdl_AfterBenignSelect()
    {
        // Regression: trailing DROP after a SELECT used to pass (only the prefix was checked).
        var result = _policy.ValidateQuery("SELECT 1; DROP TABLE users", _defaultOptions);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_BlocksMultiStatement_ByDefault()
    {
        var result = _policy.ValidateQuery("SELECT 1; SELECT 2", _defaultOptions);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_AllowsMultiStatement_WhenOptedIn()
    {
        var options = new DatabaseQueryOptions { AllowMultiStatement = true };

        var result = _policy.ValidateQuery("SELECT 1; SELECT 2", options);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("/**/DELETE FROM t")]
    [InlineData("-- comment\nDELETE FROM t")]
    [InlineData("/* drop hint */ DROP TABLE users")]
    public void ValidateQuery_NeutralizesCommentPrefix(string query)
    {
        // Regression: a comment prefix used to bypass the StartsWith keyword check.
        var result = _policy.ValidateQuery(query, _defaultOptions);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_BlocksUpdateWithoutWhere()
    {
        // Regression: UPDATE without WHERE was never covered before.
        var result = _policy.ValidateQuery("UPDATE t SET x = 1", _defaultOptions);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_AllowsUpdateWithWhere()
    {
        var result = _policy.ValidateQuery("UPDATE t SET x = 1 WHERE id = 5", _defaultOptions);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_AllowsUpdateWithoutWhere_WhenDestructiveAllowed()
    {
        var options = new DatabaseQueryOptions { AllowDestructiveDeletes = true };

        var result = _policy.ValidateQuery("UPDATE t SET x = 1", options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_DoesNotSplitOnSemicolonInsideStringLiteral()
    {
        // Non-regression: a semicolon inside a quoted value is not a statement separator.
        var result = _policy.ValidateQuery("SELECT * FROM t WHERE name = 'a;b'", _defaultOptions);

        Assert.True(result.IsValid);
    }
}
