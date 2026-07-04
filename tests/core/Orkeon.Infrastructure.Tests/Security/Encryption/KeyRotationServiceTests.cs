using System.Security.Cryptography;
using System.Text.Json;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Security.Encryption;
using Orkeon.Infrastructure.Tests.Doubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Security.Encryption;

/// <summary>
/// R2.8 — real key rotation: two-phase traversal (staging then switch), Decrypt(old) → Encrypt(new),
/// rollback on staging failure, idempotent resume (roll-forward) on switch failure, and persisted
/// key-version metadata. The nominal test fails on the pre-R2.8 no-op implementation (which logged
/// success while leaving every entry on the old key).
/// </summary>
public class KeyRotationServiceTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] RotationEmbedding = [0.1f, 0.2f, 0.3f];
    private static readonly string[] RotationTags = ["t1", "t2"];

    private static string NewKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static KeyRotationService CreateService() => new(NullLogger<KeyRotationService>.Instance);

    private static AesEncryptionProvider CreateProvider(string? keyBase64 = null, bool enabled = true)
    {
        var secrets = new FakeWritableSecretProvider();
        secrets.SetSecret("orkeon-encryption-key", keyBase64 ?? NewKeyBase64());

        return new AesEncryptionProvider(
            secrets,
            NullLogger<AesEncryptionProvider>.Instance,
            Options.Create(new AesEncryptionOptions { Enabled = enabled }));
    }

    private static async Task SeedEncryptedAsync(
        FakeMemoryStore store, IEncryptionProvider provider, string key, string plaintext)
    {
        var encrypted = await provider.EncryptStringAsync(plaintext, TestCt);
        await store.StoreAsync(key, MemoryItem.Create(encrypted, source: "rotation-test"), TestCt);
    }

    private static Dictionary<string, string> DefaultPlaintexts() => new(StringComparer.Ordinal)
    {
        ["a"] = "alpha secret payload",
        ["b"] = "bravo secret payload",
        ["c"] = "charlie secret payload",
    };

    private static async Task<(FakeMemoryStore Store, Dictionary<string, string> Plaintexts)> SeedDefaultStoreAsync(
        IEncryptionProvider oldProvider)
    {
        var store = new FakeMemoryStore();
        var plaintexts = DefaultPlaintexts();
        foreach (var (key, value) in plaintexts)
            await SeedEncryptedAsync(store, oldProvider, key, value);
        return (store, plaintexts);
    }

    // -------------------------------------------------------------------------
    // Nominal rotation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_ReEncryptsEveryEntry_NewKeyReads_OldKeyRejected()
    {
        // Arrange
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, plaintexts) = await SeedDefaultStoreAsync(oldProvider);

        // Act
        var result = await CreateService().RotateAsync(store, oldProvider, newProvider, ct: TestCt);

        // Assert — counters
        Assert.Equal(3, result.TotalEntries);
        Assert.Equal(3, result.RotatedEntries);
        Assert.Equal(0, result.AlreadyRotatedEntries);
        Assert.Equal(0, result.UnreadableEntries);
        Assert.Empty(result.Errors);

        // Assert — every entry decrypts with the NEW key and no longer with the OLD key
        // (this is the DoD test that fails on the pre-R2.8 no-op).
        foreach (var (key, expected) in plaintexts)
        {
            var stored = store.Items[key];
            Assert.Equal(expected, await newProvider.DecryptStringAsync(stored.Content, TestCt));
            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => oldProvider.DecryptStringAsync(stored.Content, TestCt));

            // Each rotated entry is stamped with the persisted key version.
            Assert.Equal(result.NewKeyVersion,
                stored.Metadata.CustomProperties?[KeyRotationDefaults.KeyVersionProperty]);
        }

        // No staging leftovers after a completed switch.
        Assert.DoesNotContain(store.Items.Keys,
            k => k.EndsWith(KeyRotationDefaults.StagingSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RotateAsync_PersistsStateMarker_WithCompletedPhaseAndKeyVersion()
    {
        // Arrange
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, _) = await SeedDefaultStoreAsync(oldProvider);

        // Act
        var result = await CreateService().RotateAsync(store, oldProvider, newProvider, ct: TestCt);

        // Assert — key-version metadata is persisted in the store (plaintext JSON marker).
        var marker = store.Items[KeyRotationDefaults.StateMarkerKey];
        var state = JsonSerializer.Deserialize<KeyRotationState>(marker.Content);
        Assert.NotNull(state);
        Assert.Equal(KeyRotationDefaults.PhaseCompleted, state.Phase);
        Assert.Equal(result.NewKeyVersion, state.KeyVersion);
        Assert.NotNull(state.CompletedAtUtc);
    }

    [Fact]
    public async Task RotateAsync_HonorsExplicitKeyVersionAndBatchSize()
    {
        // Arrange — BatchSize 2 over 3 entries exercises the key-listing pagination.
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, _) = await SeedDefaultStoreAsync(oldProvider);
        var options = new KeyRotationOptions { NewKeyVersion = "v2", BatchSize = 2 };

        // Act
        var result = await CreateService().RotateAsync(store, oldProvider, newProvider, options, TestCt);

        // Assert
        Assert.Equal("v2", result.NewKeyVersion);
        Assert.Equal(3, result.RotatedEntries);
        Assert.Equal("v2", store.Items["a"].Metadata.CustomProperties?[KeyRotationDefaults.KeyVersionProperty]);
    }

    [Fact]
    public async Task RotateAsync_PreservesIdentityEmbeddingImportanceAndMetadata()
    {
        // Arrange
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var store = new FakeMemoryStore();

        var encrypted = await oldProvider.EncryptStringAsync("payload", TestCt);
        var original = MemoryItem.Create(
            encrypted,
            embedding: RotationEmbedding,
            importance: 0.9f,
            source: "unit",
            tags: RotationTags,
            customProperties: new Dictionary<string, string> { ["foo"] = "bar" });
        await store.StoreAsync("k", original, TestCt);

        // Act
        await CreateService().RotateAsync(store, oldProvider, newProvider, ct: TestCt);

        // Assert — same entity, same metadata, new ciphertext + version stamp.
        var rotated = store.Items["k"];
        Assert.Equal(original.Id, rotated.Id);
        Assert.Equal(RotationEmbedding, rotated.Embedding);
        Assert.Equal(0.9f, rotated.Importance);
        Assert.Equal("unit", rotated.Source);
        Assert.Equal(RotationTags, rotated.Tags);
        Assert.Equal("bar", rotated.Metadata.CustomProperties?["foo"]);
        Assert.True(rotated.Metadata.CustomProperties?.ContainsKey(KeyRotationDefaults.KeyVersionProperty));
        Assert.Equal("payload", await newProvider.DecryptStringAsync(rotated.Content, TestCt));
    }

    [Fact]
    public async Task RotateAsync_EmptyStore_CompletesWithZeroCounts()
    {
        // Arrange
        var store = new FakeMemoryStore();

        // Act
        var result = await CreateService().RotateAsync(store, CreateProvider(), CreateProvider(), ct: TestCt);

        // Assert
        Assert.Equal(0, result.TotalEntries);
        Assert.Equal(0, result.RotatedEntries);
        Assert.Empty(result.Errors);
    }

    // -------------------------------------------------------------------------
    // Idempotence — no entry lost, no entry double-encrypted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_SecondRun_IsIdempotent_NoDoubleEncryption()
    {
        // Arrange
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, plaintexts) = await SeedDefaultStoreAsync(oldProvider);
        var service = CreateService();

        // Act — rotate twice with the same providers.
        var first = await service.RotateAsync(store, oldProvider, newProvider, ct: TestCt);
        var second = await service.RotateAsync(store, oldProvider, newProvider, ct: TestCt);

        // Assert — the second run re-encrypts nothing (entries classified by authenticated decryption).
        Assert.Equal(3, first.RotatedEntries);
        Assert.Equal(0, second.RotatedEntries);
        Assert.Equal(3, second.AlreadyRotatedEntries);

        // Single encryption layer: the new key still yields the ORIGINAL plaintexts.
        foreach (var (key, expected) in plaintexts)
            Assert.Equal(expected, await newProvider.DecryptStringAsync(store.Items[key].Content, TestCt));
    }

    // -------------------------------------------------------------------------
    // Staging failure — atomic rollback, store stays on the old key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_StagingFailure_RollsBack_OldKeyStillDecryptsEverything()
    {
        // Arrange — the staged copy of "b" fails to write, mid-staging.
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, plaintexts) = await SeedDefaultStoreAsync(oldProvider);
        store.FailStoreKeys.Add("b" + KeyRotationDefaults.StagingSuffix);

        // Act
        var ex = await Assert.ThrowsAsync<KeyRotationException>(
            () => CreateService().RotateAsync(store, oldProvider, newProvider, ct: TestCt));

        // Assert — rollback: no staged copies remain and EVERY original still decrypts with the OLD key.
        Assert.Contains("rolled back", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(store.Items.Keys,
            k => k.EndsWith(KeyRotationDefaults.StagingSuffix, StringComparison.Ordinal));
        foreach (var (key, expected) in plaintexts)
            Assert.Equal(expected, await oldProvider.DecryptStringAsync(store.Items[key].Content, TestCt));

        // The state marker records the rollback — never a completed phase.
        var state = JsonSerializer.Deserialize<KeyRotationState>(store.Items[KeyRotationDefaults.StateMarkerKey].Content);
        Assert.NotNull(state);
        Assert.Equal(KeyRotationDefaults.PhaseRolledBack, state.Phase);
    }

    // -------------------------------------------------------------------------
    // Switch failure — roll-forward: a re-run resumes and completes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_SwitchFailure_ThenResume_CompletesWithoutLossOrDoubleEncryption()
    {
        // Arrange — overwriting original "b" fails during the switch phase only
        // (staging writes go to "b::rotation-staging", which succeeds).
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, plaintexts) = await SeedDefaultStoreAsync(oldProvider);
        store.FailStoreKeys.Add("b");
        var service = CreateService();

        // Act 1 — first run is interrupted mid-switch ("a" switched, "b" failed, "c" pending).
        var ex = await Assert.ThrowsAsync<KeyRotationException>(
            () => service.RotateAsync(store, oldProvider, newProvider, ct: TestCt));
        Assert.Contains("resume", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Mixed state, but nothing lost: every entry decrypts with exactly one of the two keys.
        Assert.Equal(plaintexts["a"], await newProvider.DecryptStringAsync(store.Items["a"].Content, TestCt));
        Assert.Equal(plaintexts["b"], await oldProvider.DecryptStringAsync(store.Items["b"].Content, TestCt));
        Assert.Equal(plaintexts["c"], await oldProvider.DecryptStringAsync(store.Items["c"].Content, TestCt));

        // Act 2 — clear the fault and re-run with the same providers: idempotent resume.
        store.FailStoreKeys.Clear();
        var resumed = await service.RotateAsync(store, oldProvider, newProvider, ct: TestCt);

        // Assert — the resume finishes the remaining entries and never re-encrypts "a".
        Assert.Equal(1, resumed.AlreadyRotatedEntries);
        Assert.Equal(2, resumed.RotatedEntries);
        Assert.Equal(0, resumed.UnreadableEntries);

        // All entries now on the new key with their ORIGINAL plaintexts (no double encryption).
        foreach (var (key, expected) in plaintexts)
        {
            Assert.Equal(expected, await newProvider.DecryptStringAsync(store.Items[key].Content, TestCt));
            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => oldProvider.DecryptStringAsync(store.Items[key].Content, TestCt));
        }

        // Stale staging copies from the interrupted run were swept.
        Assert.DoesNotContain(store.Items.Keys,
            k => k.EndsWith(KeyRotationDefaults.StagingSuffix, StringComparison.Ordinal));

        var state = JsonSerializer.Deserialize<KeyRotationState>(store.Items[KeyRotationDefaults.StateMarkerKey].Content);
        Assert.NotNull(state);
        Assert.Equal(KeyRotationDefaults.PhaseCompleted, state.Phase);
    }

    // -------------------------------------------------------------------------
    // Unreadable entries — fail-closed by default, explicit opt-out
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_UnreadableEntry_FailsClosedByDefault_AndRollsBack()
    {
        // Arrange — "zz-raw" is plaintext (decryptable with neither key).
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, plaintexts) = await SeedDefaultStoreAsync(oldProvider);
        await store.StoreAsync("zz-raw", MemoryItem.Create("not encrypted at all", source: "rotation-test"), TestCt);

        // Act
        var ex = await Assert.ThrowsAsync<KeyRotationException>(
            () => CreateService().RotateAsync(store, oldProvider, newProvider, ct: TestCt));

        // Assert — fail-closed: nothing switched, originals stay on the old key, raw entry untouched.
        Assert.Contains("zz-raw", ex.AffectedKeys);
        foreach (var (key, expected) in plaintexts)
            Assert.Equal(expected, await oldProvider.DecryptStringAsync(store.Items[key].Content, TestCt));
        Assert.Equal("not encrypted at all", store.Items["zz-raw"].Content);
        Assert.DoesNotContain(store.Items.Keys,
            k => k.EndsWith(KeyRotationDefaults.StagingSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RotateAsync_UnreadableEntry_WithContinueOption_SkipsAndReportsIt()
    {
        // Arrange
        var oldProvider = CreateProvider();
        var newProvider = CreateProvider();
        var (store, plaintexts) = await SeedDefaultStoreAsync(oldProvider);
        await store.StoreAsync("zz-raw", MemoryItem.Create("not encrypted at all", source: "rotation-test"), TestCt);
        var options = new KeyRotationOptions { ContinueOnUnreadableEntries = true };

        // Act
        var result = await CreateService().RotateAsync(store, oldProvider, newProvider, options, TestCt);

        // Assert — readable entries rotated; the unreadable one is untouched and reported.
        Assert.Equal(4, result.TotalEntries);
        Assert.Equal(3, result.RotatedEntries);
        Assert.Equal(1, result.UnreadableEntries);
        Assert.Single(result.Errors);
        Assert.Contains("zz-raw", result.Errors[0], StringComparison.Ordinal);
        Assert.Equal("not encrypted at all", store.Items["zz-raw"].Content);
        foreach (var (key, expected) in plaintexts)
            Assert.Equal(expected, await newProvider.DecryptStringAsync(store.Items[key].Content, TestCt));
    }

    // -------------------------------------------------------------------------
    // Guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_NullArguments_Throw()
    {
        var service = CreateService();
        var store = new FakeMemoryStore();
        var provider = CreateProvider();
        var other = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.RotateAsync(null!, provider, other, ct: TestCt));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.RotateAsync(store, null!, other, ct: TestCt));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.RotateAsync(store, provider, null!, ct: TestCt));
    }

    [Fact]
    public async Task RotateAsync_SameProviderInstance_Throws()
    {
        var provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().RotateAsync(new FakeMemoryStore(), provider, provider, ct: TestCt));
    }

    [Fact]
    public async Task RotateAsync_DisabledProvider_IsFailClosed()
    {
        var enabled = CreateProvider();
        var disabled = CreateProvider(enabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().RotateAsync(new FakeMemoryStore(), enabled, disabled, ct: TestCt));
    }

    [Fact]
    public async Task RotateAsync_InvalidBatchSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateService().RotateAsync(
                new FakeMemoryStore(), CreateProvider(), CreateProvider(),
                new KeyRotationOptions { BatchSize = 0 }, TestCt));
    }

    // -------------------------------------------------------------------------
    // Key provisioning — persistence of the new key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionNewKeyAsync_GeneratesAndPersistsAUsable256BitKey()
    {
        // Arrange
        var secrets = new FakeWritableSecretProvider();

        // Act
        await CreateService().ProvisionNewKeyAsync(secrets, "orkeon-encryption-key-v2", ct: TestCt);

        // Assert — the key is persisted, well-formed, and immediately usable by a provider.
        Assert.Equal(1, secrets.StoreSecretCallCount);
        Assert.True(await secrets.ExistsAsync("orkeon-encryption-key-v2", TestCt));
        using (var secret = await secrets.GetSecretAsync("orkeon-encryption-key-v2", TestCt))
        {
            Assert.Equal(32, Convert.FromBase64String(secret.Value).Length);
        }

        var provider = new AesEncryptionProvider(
            secrets,
            NullLogger<AesEncryptionProvider>.Instance,
            Options.Create(new AesEncryptionOptions { Enabled = true, SecretName = "orkeon-encryption-key-v2" }));
        var ciphertext = await provider.EncryptStringAsync("round trip", TestCt);
        Assert.Equal("round trip", await provider.DecryptStringAsync(ciphertext, TestCt));
    }

    [Fact]
    public async Task ProvisionNewKeyAsync_RefusesToOverwriteExistingSecret()
    {
        // Arrange — overwriting a key that may still protect data would orphan its ciphertexts.
        var secrets = new FakeWritableSecretProvider();
        secrets.SetSecret("orkeon-encryption-key", NewKeyBase64());

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ProvisionNewKeyAsync(secrets, "orkeon-encryption-key", ct: TestCt));
        Assert.Equal(0, secrets.StoreSecretCallCount);
    }

    [Fact]
    public async Task ProvisionNewKeyAsync_RejectsInvalidKeySize()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateService().ProvisionNewKeyAsync(
                new FakeWritableSecretProvider(), "k", keySizeInBits: 100, ct: TestCt));
    }
}
