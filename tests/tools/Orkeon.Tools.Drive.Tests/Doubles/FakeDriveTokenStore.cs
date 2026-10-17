using System.Collections.Concurrent;
using Orkeon.Tools.Drive.Auth;

namespace Orkeon.Tools.Drive.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IDriveTokenStore"/>: tokens by key, plus counters of every read and write
/// so a test can tell a cached token from a stored one.
/// </summary>
public sealed class FakeDriveTokenStore : IDriveTokenStore
{
    private readonly ConcurrentDictionary<string, DriveTokenSet> _tokens = new(StringComparer.Ordinal);
    private int _reads;
    private int _writes;

    /// <summary>The stored tokens by key.</summary>
    public IReadOnlyDictionary<string, DriveTokenSet> Tokens => _tokens;

    /// <summary>How many reads were made.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>How many writes were made.</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>Seeds <paramref name="tokens"/> under <paramref name="key"/> without counting a write.</summary>
    public FakeDriveTokenStore With(string key, DriveTokenSet tokens)
    {
        _tokens[key] = tokens;
        return this;
    }

    /// <inheritdoc />
    public Task<DriveTokenSet?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_tokens.TryGetValue(key, out var tokens) ? tokens : null);
    }

    /// <inheritdoc />
    public Task WriteAsync(string key, DriveTokenSet tokens, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _writes);
        _tokens[key] = tokens;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_tokens.TryRemove(key, out _));
}
