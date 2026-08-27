using System.Security.Cryptography;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Security.Encryption;

/// <summary>
/// Simple in-memory IMemoryProvider for testing decorators.
/// </summary>
internal sealed class FakeMemoryProvider : IMemoryProvider
{
    private readonly Dictionary<string, MemoryItem> _store = [];

    /// <summary>Exposes raw stored items for assertions.</summary>
    public IReadOnlyDictionary<string, MemoryItem> Store => _store;

    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        _store[key] = item;
        return Task.CompletedTask;
    }

    public Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(key, out var item);
        return Task.FromResult(item);
    }

    public Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var results = _store.Values
            .Where(i => i.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit);
        return Task.FromResult(results);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Remove(key));

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _store.Clear();
        return Task.CompletedTask;
    }
}

public class EncryptedMemoryProviderTests
{
    private static readonly string[] Tag1Tag2 = ["tag1", "tag2"];

    private static readonly string TestKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

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
    public async Task Store_EncryptsContent_BeforeStoring()
    {
        var inner = new FakeMemoryProvider();
        var encryption = CreateEncryption();
        var provider = new EncryptedMemoryProviderDecorator(
            inner, encryption, NullLogger<EncryptedMemoryProviderDecorator>.Instance);

        var item = MemoryItem.Create("secret agent data", importance: 0.9f, source: "test");
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        // The inner store should have encrypted content, NOT the plaintext
        var storedItem = inner.Store["key1"];
        Assert.NotEqual("secret agent data", storedItem.Content);
        // Content should be Base64 (encrypted)
        Assert.DoesNotContain("secret", storedItem.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_DecryptsContent_AfterRetrieval()
    {
        var inner = new FakeMemoryProvider();
        var encryption = CreateEncryption();
        var provider = new EncryptedMemoryProviderDecorator(
            inner, encryption, NullLogger<EncryptedMemoryProviderDecorator>.Instance);

        var item = MemoryItem.Create("confidential info", importance: 0.8f, source: "test");
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal("confidential info", retrieved!.Content);
    }

    [Fact]
    public async Task Store_WhenDisabled_DelegatesDirectly()
    {
        var inner = new FakeMemoryProvider();
        var encryption = CreateEncryption(enabled: false);
        var provider = new EncryptedMemoryProviderDecorator(
            inner, encryption, NullLogger<EncryptedMemoryProviderDecorator>.Instance);

        var item = MemoryItem.Create("plaintext data", importance: 0.5f, source: "test");
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        // When disabled, content should be stored as-is
        var storedItem = inner.Store["key1"];
        Assert.Equal("plaintext data", storedItem.Content);
    }

    [Fact]
    public async Task RoundTrip_StoreAndRetrieve_ContentPreserved()
    {
        var inner = new FakeMemoryProvider();
        var encryption = CreateEncryption();
        var provider = new EncryptedMemoryProviderDecorator(
            inner, encryption, NullLogger<EncryptedMemoryProviderDecorator>.Instance);

        var originalContent = "Multi-line\nmemory content\nwith unicode: café résumé";
        var item = MemoryItem.Create(originalContent, importance: 0.7f, source: "test", tags: Tag1Tag2);
        await provider.StoreAsync("roundtrip-key", item, TestContext.Current.CancellationToken);

        var retrieved = await provider.GetAsync("roundtrip-key", TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal(originalContent, retrieved!.Content);
        Assert.Equal(0.7f, retrieved.Importance);
    }

    /// <summary>
    /// Everything but the content survives the round trip — the class's own contract is
    /// "metadata and embeddings are NOT encrypted (needed for search/indexing)".
    /// <para>
    /// Four paths rebuilt the item inline with five of its seven fields, so
    /// <c>CustomProperties</c> and <c>CreatedBy</c> were silently dropped on the ordinary
    /// store/get/search — RAG's <c>rag.kind</c>/<c>meta.lang</c> among them, which is what
    /// makes a chunk findable. The collection-scoped paths kept them and had the test that
    /// said so; the primary ones had tests asserting content, embedding, score and key.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RoundTrip_PreservesMetadataTheDecoratorPromisesNotToTouch()
    {
        var inner = new FakeMemoryProvider();
        var encryption = CreateEncryption();
        var provider = new EncryptedMemoryProviderDecorator(
            inner, encryption, NullLogger<EncryptedMemoryProviderDecorator>.Instance);

        var author = Orkeon.Domain.Common.AgentId.Create();
        var item = MemoryItem.Create(
            "confidential",
            importance: 0.4f,
            source: "rag",
            tags: Tag1Tag2,
            createdBy: author,
            customProperties: new Dictionary<string, string> { ["rag.kind"] = "chunk", ["meta.lang"] = "fr" });

        await provider.StoreAsync("meta-key", item, TestContext.Current.CancellationToken);

        // Stored encrypted, metadata in clear — that is the whole point of the decorator.
        var stored = inner.Store["meta-key"];
        Assert.NotEqual("confidential", stored.Content);
        Assert.Equal("chunk", stored.Metadata.CustomProperties?["rag.kind"]);
        Assert.Equal(author, stored.Metadata.CreatedBy);

        var retrieved = await provider.GetAsync("meta-key", TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal("confidential", retrieved!.Content);
        Assert.Equal("chunk", retrieved.Metadata.CustomProperties?["rag.kind"]);
        Assert.Equal("fr", retrieved.Metadata.CustomProperties?["meta.lang"]);
        Assert.Equal(author, retrieved.Metadata.CreatedBy);
        Assert.Equal("rag", retrieved.Metadata.Source);
    }
}
