using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Infrastructure.Security.Encryption;
using Orkeon.Infrastructure.Tests.Security.Encryption;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory.Sqlite;

/// <summary>
/// Proves the SQLite provider is eligible for at-rest encryption via
/// <see cref="EncryptedMemoryProviderDecorator"/> (R3.2): content is ciphertext in the
/// SQLite store, decrypted on retrieval, and vector search still works because
/// embeddings stay clear.
/// </summary>
public sealed class SqliteMemoryProviderEncryptionTests : IDisposable
{
    private static readonly string TestKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static readonly float[] VectorSecretEmbedding = [1f, 0f];

    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private readonly SqliteMemoryProvider _sqlite;
    private readonly EncryptedMemoryProviderDecorator _encrypted;

    public SqliteMemoryProviderEncryptionTests()
    {
        _sqlite = new SqliteMemoryProvider(
            Options.Create(new SqliteMemoryOptions { ConnectionString = "Data Source=:memory:" }),
            new FakeFileSystemService());
        _encrypted = new EncryptedMemoryProviderDecorator(
            _sqlite, CreateEncryption(), NullLogger<EncryptedMemoryProviderDecorator>.Instance);
    }

    public void Dispose() => _sqlite.Dispose();

    private static AesEncryptionProvider CreateEncryption(bool enabled = true)
    {
        var secrets = new InMemorySecretProvider();
        secrets.SetSecret("orkeon-encryption-key", TestKeyBase64);

        return new AesEncryptionProvider(
            secrets,
            NullLogger<AesEncryptionProvider>.Instance,
            Options.Create(new AesEncryptionOptions { Enabled = enabled }));
    }

    [Fact]
    public async Task Store_ThroughDecorator_PersistsCiphertextInSqlite()
    {
        var item = MemoryItem.Create("top secret payload", importance: 0.9f, source: "test");

        await _encrypted.StoreAsync("key-1", item, TestCt);

        // Reading the inner SQLite store directly must expose ciphertext, not plaintext.
        var raw = await _sqlite.GetAsync("key-1", TestCt);
        Assert.NotNull(raw);
        Assert.NotEqual("top secret payload", raw.Content);
        Assert.DoesNotContain("secret", raw.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_ThroughDecorator_DecryptsContentFromSqlite()
    {
        var item = MemoryItem.Create("confidential info", importance: 0.7f, source: "test");

        await _encrypted.StoreAsync("key-1", item, TestCt);
        var retrieved = await _encrypted.GetAsync("key-1", TestCt);

        Assert.NotNull(retrieved);
        Assert.Equal("confidential info", retrieved.Content);
    }

    [Fact]
    public async Task StoreWithEmbedding_ThroughDecorator_KeepsEmbeddingClear_AndEncryptsContent()
    {
        var item = MemoryItem.Create("vector secret", importance: 0.6f, source: "test");

        await _encrypted.StoreWithEmbeddingAsync("key-1", item, [1f, 0f], TestCt);

        var raw = await _sqlite.GetAsync("key-1", TestCt);
        Assert.NotNull(raw);
        Assert.NotEqual("vector secret", raw.Content);
        Assert.Equal(VectorSecretEmbedding, raw.Embedding);
    }

    [Fact]
    public async Task SearchSimilar_ThroughDecorator_ReturnsDecryptedScoredResults()
    {
        await _encrypted.StoreWithEmbeddingAsync(
            "key-1", MemoryItem.Create("encrypted but findable", source: "test"), [1f, 0f], TestCt);
        await _encrypted.StoreWithEmbeddingAsync(
            "key-2", MemoryItem.Create("orthogonal entry", source: "test"), [0f, 1f], TestCt);

        var results = await _encrypted.SearchSimilarAsync([1f, 0f], topK: 1, minScore: 0.5f, cancellationToken: TestCt);

        // Vector scoring happens on clear embeddings; content comes back decrypted.
        Assert.Single(results);
        Assert.Equal("encrypted but findable", results[0].Item.Content);
        Assert.True(results[0].Score >= 0.5f);
    }

    [Fact]
    public async Task Decorator_WithEncryptionDisabled_PassesThrough()
    {
        var passthrough = new EncryptedMemoryProviderDecorator(
            _sqlite, CreateEncryption(enabled: false), NullLogger<EncryptedMemoryProviderDecorator>.Instance);

        await passthrough.StoreAsync("key-1", MemoryItem.Create("plain payload"), TestCt);

        var raw = await _sqlite.GetAsync("key-1", TestCt);
        Assert.NotNull(raw);
        Assert.Equal("plain payload", raw.Content);
    }
}
