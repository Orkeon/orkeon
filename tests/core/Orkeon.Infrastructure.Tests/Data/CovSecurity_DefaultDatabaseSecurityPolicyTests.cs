using Orkeon.Application.Abstractions.Data;
using Orkeon.Infrastructure.Data;

namespace Orkeon.Infrastructure.Tests.CovSecurity;

/// <summary>
/// Coverage for <see cref="DefaultDatabaseSecurityPolicy"/>.
/// </summary>
public sealed class CovSecurity_DefaultDatabaseSecurityPolicyTests
{
    private readonly DefaultDatabaseSecurityPolicy _sut = new();

    private static readonly DatabaseQueryOptions Restrictive = new();
    private static readonly DatabaseQueryOptions PermissiveDdl = new() { AllowDdl = true };
    private static readonly DatabaseQueryOptions PermissiveDeletes = new() { AllowDestructiveDeletes = true };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateQuery_ShouldFail_WhenQueryEmpty(string? query)
    {
        var result = _sut.ValidateQuery(query!, Restrictive);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateQuery_ShouldSucceed_ForPlainSelect()
    {
        var result = _sut.ValidateQuery("SELECT * FROM users WHERE id = 1", Restrictive);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("CREATE TABLE x (id int)")]
    [InlineData("ALTER TABLE x ADD col int")]
    [InlineData("GRANT SELECT ON x TO y")]
    [InlineData("REVOKE SELECT ON x FROM y")]
    public void ValidateQuery_ShouldBlockDdl_WhenNotAllowed(string query)
    {
        var result = _sut.ValidateQuery(query, Restrictive);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldBlockBareKeyword()
    {
        var result = _sut.ValidateQuery("DROP", Restrictive);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldBlockKeywordTerminatedBySemicolon()
    {
        var result = _sut.ValidateQuery("DROP;", Restrictive);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldAllowDdl_WhenAllowDdlTrue()
    {
        var result = _sut.ValidateQuery("CREATE TABLE x (id int)", PermissiveDdl);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldBlockAlterTableDrop_Compound()
    {
        var result = _sut.ValidateQuery("ALTER TABLE x DROP COLUMN y", Restrictive);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("ALTER", StringComparison.Ordinal));
    }

    [Fact]
    public void IsDestructiveOperation_ShouldDetectAlterTableDropCompound()
    {
        // Reaches the compound "ALTER TABLE ... DROP" branch (keyword check
        // returns the standalone ALTER first inside ValidateQuery, but
        // IsDestructiveOperation exercises the same CheckDdl logic).
        Assert.True(_sut.IsDestructiveOperation("ALTER TABLE x DROP COLUMN y"));
    }

    [Fact]
    public void ValidateQuery_ShouldBlockDeleteWithoutWhere()
    {
        var result = _sut.ValidateQuery("DELETE FROM users", Restrictive);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateQuery_ShouldAllowDeleteWithWhere()
    {
        var result = _sut.ValidateQuery("DELETE FROM users WHERE id = 1", Restrictive);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldAllowDestructiveDelete_WhenOptionEnabled()
    {
        var result = _sut.ValidateQuery("DELETE FROM users", PermissiveDeletes);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateQuery_ShouldNormalizeWhitespaceAndCase()
    {
        // mixed case + extra whitespace must still match DROP
        var result = _sut.ValidateQuery("  drop    table   users  ", Restrictive);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void IsDestructiveOperation_ShouldDetectDrop()
    {
        Assert.True(_sut.IsDestructiveOperation("DROP TABLE x"));
    }

    [Fact]
    public void IsDestructiveOperation_ShouldDetectTruncate()
    {
        Assert.True(_sut.IsDestructiveOperation("TRUNCATE TABLE x"));
    }

    [Fact]
    public void IsDestructiveOperation_ShouldDetectDeleteWithoutWhere()
    {
        Assert.True(_sut.IsDestructiveOperation("DELETE FROM x"));
    }

    [Fact]
    public void IsDestructiveOperation_ShouldReturnFalse_ForSafeSelect()
    {
        Assert.False(_sut.IsDestructiveOperation("SELECT 1"));
    }

    [Fact]
    public void IsDestructiveOperation_ShouldReturnFalse_ForDeleteWithWhere()
    {
        Assert.False(_sut.IsDestructiveOperation("DELETE FROM x WHERE id = 1"));
    }
}
