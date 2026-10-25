using Orkeon.Tools.Data.Relational;

namespace Orkeon.Tools.Data.Tests.Relational;

/// <summary>Tests for <see cref="DatabaseProviderFactory"/>: provider names, aliases and errors.</summary>
public class DatabaseProviderFactoryTests
{
    [Theory]
    [InlineData("Microsoft.Data.SqlClient", typeof(Microsoft.Data.SqlClient.SqlClientFactory))]
    [InlineData("sqlserver", typeof(Microsoft.Data.SqlClient.SqlClientFactory))]
    [InlineData("Npgsql", typeof(Npgsql.NpgsqlFactory))]
    [InlineData("PostgreSQL", typeof(Npgsql.NpgsqlFactory))]
    [InlineData("MySqlConnector", typeof(MySqlConnector.MySqlConnectorFactory))]
    [InlineData("MYSQL", typeof(MySqlConnector.MySqlConnectorFactory))]
    [InlineData("Microsoft.Data.Sqlite", typeof(Microsoft.Data.Sqlite.SqliteFactory))]
    [InlineData("SQLite", typeof(Microsoft.Data.Sqlite.SqliteFactory))]
    public void ShouldReturnProviderFactory_WhenNameOrAliasIsKnown(string name, Type expected)
    {
        var factory = new DatabaseProviderFactory();

        Assert.IsType(expected, factory.GetProvider(name));
        Assert.True(factory.IsSupported(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldThrowArgumentException_WhenNameIsBlank(string name)
    {
        var factory = new DatabaseProviderFactory();

        Assert.Throws<ArgumentException>(() => factory.GetProvider(name));
        Assert.False(factory.IsSupported(name));
    }

    [Fact]
    public void ShouldThrowListingSupportedProviders_WhenNameIsUnknown()
    {
        var factory = new DatabaseProviderFactory();

        var ex = Assert.Throws<NotSupportedException>(() => factory.GetProvider("Oracle"));

        Assert.Contains("'Oracle'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Data.Sqlite", ex.Message, StringComparison.Ordinal);
        Assert.False(factory.IsSupported("Oracle"));
        Assert.Equal(4, factory.SupportedProviders.Count);
    }
}
