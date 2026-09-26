using System.Collections.Concurrent;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IEmailTokenStore"/>: tokens by key, plus counters of every read, write and
/// delete so a test can tell a cached token from a stored one.
/// </summary>
public sealed class FakeEmailTokenStore : IEmailTokenStore
{
    private readonly ConcurrentDictionary<string, EmailTokenSet> _tokens = new(StringComparer.Ordinal);
    private int _reads;
    private int _writes;
    private int _deletes;

    /// <summary>The stored tokens by key.</summary>
    public IReadOnlyDictionary<string, EmailTokenSet> Tokens => _tokens;

    /// <summary>How many reads were made.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>How many writes were made.</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>How many deletes were made.</summary>
    public int Deletes => Volatile.Read(ref _deletes);

    /// <summary>Seeds <paramref name="tokens"/> under <paramref name="key"/> without counting a write.</summary>
    public FakeEmailTokenStore With(string key, EmailTokenSet tokens)
    {
        _tokens[key] = tokens;
        return this;
    }

    /// <inheritdoc />
    public Task<EmailTokenSet?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_tokens.TryGetValue(key, out var tokens) ? tokens : null);
    }

    /// <inheritdoc />
    public Task WriteAsync(string key, EmailTokenSet tokens, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _writes);
        _tokens[key] = tokens;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deletes);
        return Task.FromResult(_tokens.TryRemove(key, out _));
    }
}
