using Orkeon.Tools.Data.Relational;

namespace Orkeon.Tools.Data.Tests.Relational;

/// <summary>
/// The identifier allowlist of <see cref="DatabaseSchemaTool"/>: a table or index whose name falls
/// outside <c>^[a-zA-Z0-9_]+$</c> is skipped. The names themselves reach SQLite as bound parameters.
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
}
