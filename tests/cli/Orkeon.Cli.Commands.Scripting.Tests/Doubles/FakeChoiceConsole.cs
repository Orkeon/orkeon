using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary><see cref="IChoiceConsole"/> answering what it is configured to, and recording what it is asked.</summary>
internal sealed class FakeChoiceConsole : IConsoleAdapter, IChoiceConsole
{
    public bool CanChoose { get; set; } = true;

    /// <summary>What the panel answers; null is Esc.</summary>
    public ConsoleChoiceAnswer? Answer { get; set; }

    /// <summary>The request received.</summary>
    public ConsoleChoiceRequest? Request { get; private set; }

    /// <summary>What a read answers.</summary>
    public string? Line { get; set; }

    /// <summary>The reads that went through the blocking <see cref="ReadLine"/>.</summary>
    public int BlockingReads { get; private set; }

    /// <summary>The reads that went through the cancellable <see cref="ReadLineAsync"/>.</summary>
    public int CancellableReads { get; private set; }

    /// <summary>The reads made for a script's own question.</summary>
    public int AnswerReads { get; private set; }

    /// <summary>When true, a cancellable read stays open until its token cancels, then answers null.</summary>
    public bool HoldReads { get; set; }

    /// <summary>Completes at the first read of either kind.</summary>
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ConsoleChoiceAnswer?> ChooseAsync(ConsoleChoiceRequest request, CancellationToken cancellationToken)
    {
        Request = request;
        return Task.FromResult(Answer);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        CancellableReads++;
        ReadStarted.TrySetResult();
        if (!HoldReads) return Task.FromResult(Line);
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => held.TrySetResult(null));
        return held.Task;
    }

    public Task<string?> ReadAnswerAsync(CancellationToken cancellationToken)
    {
        AnswerReads++;
        return ReadLineAsync(cancellationToken);
    }

    public void Write(string text)
    {
    }

    public void WriteLine(string text)
    {
    }

    public string? ReadLine()
    {
        BlockingReads++;
        ReadStarted.TrySetResult();
        return Line;
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
    }
}
