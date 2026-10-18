namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Hand-written <see cref="IAsyncDisposable"/> double: records how many times it was disposed and,
/// when <see cref="DisposalLog"/> is set, appends its <see cref="Name"/> there so a test can read the
/// order resources were released in. <see cref="ThrowOnDispose"/> makes the disposal fail.
/// </summary>
public sealed class FakeAsyncDisposable : IAsyncDisposable
{
    /// <summary>A name the disposal log records.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Where the disposal appends <see cref="Name"/>; shared by the resources of one test.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string>? DisposalLog { get; init; }

    /// <summary>When set, the disposal throws this exception (after counting and logging).</summary>
    public Exception? ThrowOnDispose { get; init; }

    /// <summary>How many times <see cref="DisposeAsync"/> ran.</summary>
    public int DisposeCount;

    /// <summary>Whether <see cref="DisposeAsync"/> ran at least once.</summary>
    public bool IsDisposed => Volatile.Read(ref DisposeCount) > 0;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Yield first, so a release that awaits a real resource is exercised.
        await Task.Yield();
        Interlocked.Increment(ref DisposeCount);
        DisposalLog?.Enqueue(Name);
        if (ThrowOnDispose is not null)
            throw ThrowOnDispose;
    }
}
