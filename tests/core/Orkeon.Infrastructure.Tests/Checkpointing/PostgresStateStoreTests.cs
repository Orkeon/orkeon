using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

public class PostgresStateStoreTests
{
    private static IOptions<PostgresStateStoreOptions> CreateOptions(string schemaName) =>
        Options.Create(new PostgresStateStoreOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            SchemaName = schemaName,
            AutoMigrate = false
        });

    // ── Empty / whitespace schema names ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrowArgumentException_WhenSchemaNameIsEmpty(string invalidSchema)
    {
        // Arrange
        var options = CreateOptions(invalidSchema);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new PostgresStateStore(options));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Max length exceeded ──

    [Fact]
    public void Constructor_ShouldThrowArgumentException_WhenSchemaNameExceedsMaxLength()
    {
        // Arrange — 64 characters (1 over the PostgreSQL 63-byte limit)
        var longSchema = new string('a', 64);
        var options = CreateOptions(longSchema);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new PostgresStateStore(options));
        Assert.Contains("63", ex.Message);
    }

    // ── Invalid characters ──

    [Theory]
    [InlineData("schema;drop")]
    [InlineData("orkeon\"; DROP TABLE users; --")]
    [InlineData("my-schema")]
    [InlineData("my schema")]
    [InlineData("123schema")]
    [InlineData("schema.name")]
    [InlineData("schema'name")]
    public void Constructor_ShouldThrowArgumentException_WhenSchemaNameContainsInvalidCharacters(string invalidSchema)
    {
        // Arrange
        var options = CreateOptions(invalidSchema);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new PostgresStateStore(options));
        Assert.Contains("alphanumeric", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Reserved keywords ──

    [Theory]
    [InlineData("public")]
    [InlineData("PUBLIC")]
    [InlineData("information_schema")]
    [InlineData("pg_catalog")]
    [InlineData("schema")]
    [InlineData("all")]
    [InlineData("current_user")]
    [InlineData("session_user")]
    [InlineData("user")]
    public void Constructor_ShouldThrowArgumentException_WhenSchemaNameIsReservedKeyword(string reservedSchema)
    {
        // Arrange
        var options = CreateOptions(reservedSchema);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new PostgresStateStore(options));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Valid schema names ──

    [Theory]
    [InlineData("orkeon")]
    [InlineData("my_schema")]
    [InlineData("orkeon_v1")]
    [InlineData("MySchema")]
    [InlineData("a")]
    public void Constructor_ShouldAcceptValidSchemaName(string validSchema)
    {
        // Arrange
        var options = CreateOptions(validSchema);

        // Act & Assert (should not throw)
        var store = new PostgresStateStore(options);
        Assert.NotNull(store);
        store.Dispose();
    }

    // ── Schema name starting with underscore ──

    [Theory]
    [InlineData("_schema")]
    [InlineData("_private")]
    [InlineData("__double_underscore")]
    public void Constructor_ShouldAcceptSchemaNameStartingWithUnderscore(string validSchema)
    {
        // Arrange
        var options = CreateOptions(validSchema);

        // Act & Assert (should not throw)
        var store = new PostgresStateStore(options);
        Assert.NotNull(store);
        store.Dispose();
    }

    // ── Exactly 63 characters (boundary) ──

    [Fact]
    public void Constructor_ShouldAcceptSchemaNameWithExactly63Characters()
    {
        // Arrange — exactly at the PostgreSQL limit
        var schema63 = new string('a', 63);
        var options = CreateOptions(schema63);

        // Act & Assert (should not throw)
        var store = new PostgresStateStore(options);
        Assert.NotNull(store);
        store.Dispose();
    }
}
