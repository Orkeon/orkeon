using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Console;

/// <summary>
/// R10.10 (ANT-010): <see cref="IConsoleAdapter.ReadLineAsync"/> and
/// <see cref="IConsoleAdapter.ReadKeyAsync"/> are default interface methods, so an
/// implementation that only provides the historical synchronous surface inherits async
/// variants that delegate to it — fully non-breaking. These tests pin that delegation.
/// </summary>
public sealed class ConsoleAdapterDefaultAsyncMethodsTests
{
    /// <summary>
    /// Hand-written stub implementing only the synchronous members, so calls through the
    /// interface exercise the default async implementations.
    /// </summary>
    private sealed class StubSyncOnlyConsoleAdapter : IConsoleAdapter
    {
        public string? NextLine { get; set; }
        public ConsoleKeyInfo NextKey { get; set; }
        public bool? LastIntercept { get; private set; }
        public int ReadLineCalls { get; private set; }

        public void Write(string text) { }
        public void WriteLine(string text) { }
        public void Clear() { }

        public string? ReadLine()
        {
            ReadLineCalls++;
            return NextLine;
        }

        public ConsoleKeyInfo ReadKey(bool intercept = false)
        {
            LastIntercept = intercept;
            return NextKey;
        }
    }

    [Fact]
    public async Task ReadLineAsync_default_delegates_to_sync_ReadLine()
    {
        var stub = new StubSyncOnlyConsoleAdapter { NextLine = "hello" };
        IConsoleAdapter adapter = stub;

        var line = await adapter.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.Equal("hello", line);
        Assert.Equal(1, stub.ReadLineCalls);
    }

    [Fact]
    public async Task ReadLineAsync_default_propagates_null_end_of_input()
    {
        IConsoleAdapter adapter = new StubSyncOnlyConsoleAdapter { NextLine = null };

        var line = await adapter.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.Null(line);
    }

    [Fact]
    public async Task ReadKeyAsync_default_delegates_to_sync_ReadKey_with_intercept()
    {
        var stub = new StubSyncOnlyConsoleAdapter
        {
            NextKey = new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false),
        };
        IConsoleAdapter adapter = stub;

        var key = await adapter.ReadKeyAsync(intercept: true, TestContext.Current.CancellationToken);

        Assert.Equal(ConsoleKey.A, key.Key);
        Assert.True(stub.LastIntercept);
    }

    [Fact]
    public async Task ReadLineAsync_default_returns_canceled_task_for_an_already_canceled_token()
    {
        var stub = new StubSyncOnlyConsoleAdapter { NextLine = "never-read" };
        IConsoleAdapter adapter = stub;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => adapter.ReadLineAsync(cts.Token));

        Assert.Equal(0, stub.ReadLineCalls);
    }

    [Fact]
    public async Task ReadKeyAsync_default_returns_canceled_task_for_an_already_canceled_token()
    {
        IConsoleAdapter adapter = new StubSyncOnlyConsoleAdapter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => adapter.ReadKeyAsync(intercept: false, cts.Token));
    }
}
