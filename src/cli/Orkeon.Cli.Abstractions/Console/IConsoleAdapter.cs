namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// Abstracts System.Console operations to enable interactive testing.
/// </summary>
/// <remarks>
/// The synchronous members are the historical contract (ANT-010: assumed-blocking by design).
/// The asynchronous members are default interface methods that delegate to their synchronous
/// counterparts, so every existing implementation keeps compiling unchanged; implementations
/// backed by a real asynchronous input pipeline (e.g. the Terminal.Gui adapter) should
/// override them to await input without pinning a thread.
/// </remarks>
public interface IConsoleAdapter
{
    void Write(string text);
    void WriteLine(string text);
    string? ReadLine();
    ConsoleKeyInfo ReadKey(bool intercept = false);
    void Clear();

    /// <summary>
    /// Reads a line of input asynchronously. The default implementation delegates to the
    /// synchronous <see cref="ReadLine"/> and therefore still blocks the calling thread until
    /// a line is available — the token is only honoured before the read starts. Override this
    /// in implementations that can genuinely await input.
    /// </summary>
    /// <param name="cancellationToken">
    /// Observed before delegating (a cancelled token yields a cancelled task); the blocking
    /// synchronous read itself is not interruptible.
    /// </param>
    /// <returns>The submitted line, or <c>null</c> at end of input.</returns>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<string?>(cancellationToken);
        return Task.FromResult(ReadLine());
    }

    /// <summary>
    /// Reads a key press asynchronously. The default implementation delegates to the
    /// synchronous <see cref="ReadKey"/> and therefore still blocks the calling thread until
    /// a key arrives — the token is only honoured before the read starts. Override this in
    /// implementations that can genuinely await input.
    /// </summary>
    /// <param name="intercept">When <c>true</c>, the pressed key is not echoed.</param>
    /// <param name="cancellationToken">
    /// Observed before delegating (a cancelled token yields a cancelled task); the blocking
    /// synchronous read itself is not interruptible.
    /// </param>
    Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept = false, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ConsoleKeyInfo>(cancellationToken);
        return Task.FromResult(ReadKey(intercept));
    }
}
