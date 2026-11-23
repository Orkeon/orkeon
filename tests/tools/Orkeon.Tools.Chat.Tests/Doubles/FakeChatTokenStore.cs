using System.Collections.Concurrent;
using Orkeon.Tools.Chat.Auth;

namespace Orkeon.Tools.Chat.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IChatTokenStore"/>: tokens by key, plus counters of every read and write
/// so a test can tell a cached token from a stored one.
/// </summary>
public sealed class FakeChatTokenStore : IChatTokenStore
{
    private readonly ConcurrentDictionary<string, ChatTokenSet> _tokens = new(StringComparer.Ordinal);
    private int _reads;
    private int _writes;

    /// <summary>The stored tokens by key.</summary>
    public IReadOnlyDictionary<string, ChatTokenSet> Tokens => _tokens;

    /// <summary>How many reads were made.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>How many writes were made.</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>Seeds <paramref name="tokens"/> under <paramref name="key"/> without counting a write.</summary>
    public FakeChatTokenStore With(string key, ChatTokenSet tokens)
    {
        _tokens[key] = tokens;
        return this;
    }

    /// <summary>Removes <paramref name="key"/> as another process would.</summary>
    public void Drop(string key) => _tokens.TryRemove(key, out _);

    /// <inheritdoc />
    public Task<ChatTokenSet?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_tokens.TryGetValue(key, out var tokens) ? tokens : null);
    }

    /// <inheritdoc />
    public Task WriteAsync(string key, ChatTokenSet tokens, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _writes);
        _tokens[key] = tokens;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_tokens.TryRemove(key, out _));

    /// <inheritdoc />
    public Task<bool> DeleteAccountAsync(string account, string? keptKey, CancellationToken cancellationToken)
    {
        var keys = _tokens.Keys.Where(key => key != keptKey
            && key.Length == account.Length + 13
            && key.StartsWith(account + "-", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keys)
            _tokens.TryRemove(key, out _);
        return Task.FromResult(keys.Count > 0);
    }
}
