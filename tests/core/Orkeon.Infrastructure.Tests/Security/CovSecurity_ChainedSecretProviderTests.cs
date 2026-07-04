using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Tests.Doubles;
using ChainedSut = Orkeon.Infrastructure.Security.Secrets.ChainedSecretProvider;

namespace Orkeon.Infrastructure.Tests.CovSecurity;

/// <summary>
/// Additional coverage for <see cref="ChainedSut"/>: argument guards and the
/// exception/cancellation branches of <c>ExistsAsync</c> / <c>ListSecretNamesAsync</c>
/// that the existing suite does not exercise.
/// </summary>
public sealed class CovSecurity_ChainedSecretProviderTests
{
    private readonly ILogger<ChainedSut> _logger = NullLogger<ChainedSut>.Instance;

    private ChainedSut Create(params ISecretProvider[] providers) => new(providers, _logger);

    [Fact]
    public void Ctor_ShouldThrow_WhenProvidersNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ChainedSut(null!, _logger));
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenLoggerNull()
    {
        var providers = new ISecretProvider[] { new MockSecretProvider() };
        Assert.Throws<ArgumentNullException>(
            () => new ChainedSut(providers, null!));
    }

    [Fact]
    public async Task GetSecretAsync_ShouldThrow_WhenNameNull()
    {
        var sut = Create(new MockSecretProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.GetSecretAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldThrow_WhenNameNull()
    {
        var sut = Create(new MockSecretProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.ExistsAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenNoProviderHasSecret()
    {
        var sut = Create(new MockSecretProvider(), new MockSecretProvider());

        Assert.False(await sut.ExistsAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldContinue_WhenProviderThrows()
    {
        var failing = new ThrowingSecretProvider(new InvalidOperationException("boom"));
        var working = new MockSecretProvider();
        working.AddSecret("key", "value");

        var sut = Create(failing, working);

        Assert.True(await sut.ExistsAsync("key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldRethrow_WhenProviderCancels()
    {
        var failing = new ThrowingSecretProvider(new OperationCanceledException());
        var sut = Create(failing);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ExistsAsync("key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldSkipFailingProvider()
    {
        var failing = new ThrowingSecretProvider(new InvalidOperationException("boom"));
        var working = new MockSecretProvider();
        working.AddSecret("KEY_A", "a");

        var sut = Create(failing, working);

        var names = await sut.ListSecretNamesAsync(TestContext.Current.CancellationToken);

        Assert.Single(names);
        Assert.Contains("KEY_A", names);
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldRethrow_WhenProviderCancels()
    {
        var failing = new ThrowingSecretProvider(new OperationCanceledException());
        var sut = Create(failing);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ListSecretNamesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Test double whose every operation throws the configured exception.</summary>
    private sealed class ThrowingSecretProvider : ISecretProvider
    {
        private readonly Exception _ex;
        public ThrowingSecretProvider(Exception ex) => _ex = ex;

        public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default) => throw _ex;
        public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default) => throw _ex;
        public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default) => throw _ex;
    }
}
