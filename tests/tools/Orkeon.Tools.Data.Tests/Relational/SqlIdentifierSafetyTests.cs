using Orkeon.Tools.Data.Relational;

namespace Orkeon.Tools.Data.Tests.Relational;

/// <summary>
/// Proves the SQL-identifier safety invariant that backs the S2077 security hotspots in
/// <see cref="DatabaseSchemaTool"/>. SQLite PRAGMA statements cannot bind parameters, so table and
/// index identifiers must be interpolated — but only after
/// <see cref="DatabaseSchemaTool.IsSafeIdentifier"/> allow-lists them (<c>^[a-zA-Z0-9_]+$</c>) and
/// <see cref="DatabaseSchemaTool.QuoteSqliteIdentifier"/> double-quotes/escapes them. These tests make
/// the invariant executable rather than only documented in the suppression justifications.
/// </summary>
public sealed class SqlIdentifierSafetyTests
{
    [Theory]
    [InlineData("users")]
    [InlineData("memory_items")]
    [InlineData("Table1")]
    [InlineData("_private")]
    [InlineData("a0_Z")]
    public void IsSafeIdentifier_AcceptsAllowlistedIdentifiers(string identifier)
        => Assert.True(DatabaseSchemaTool.IsSafeIdentifier(identifier));

    [Theory]
    [InlineData("")]
    [InlineData("users; DROP TABLE secrets")]
    [InlineData("users'--")]
    [InlineData("name = 1 OR 1=1")]
    [InlineData("a b")]
    [InlineData("table.column")]
    [InlineData("\"quoted\"")]
    [InlineData("col)")]
    [InlineData("../etc")]
    [InlineData("t_é")]
    public void IsSafeIdentifier_RejectsInjectionAndNonAllowlisted(string identifier)
        => Assert.False(DatabaseSchemaTool.IsSafeIdentifier(identifier));

    [Fact]
    public void QuoteSqliteIdentifier_WrapsInDoubleQuotes()
        => Assert.Equal("\"users\"", DatabaseSchemaTool.QuoteSqliteIdentifier("users"));

    [Fact]
    public void QuoteSqliteIdentifier_DoublesEmbeddedQuotes_SoTheIdentifierCannotBreakOut()
        => Assert.Equal("\"a\"\"b\"", DatabaseSchemaTool.QuoteSqliteIdentifier("a\"b"));
}
