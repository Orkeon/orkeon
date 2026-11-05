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

    public Task<ConsoleChoiceAnswer?> ChooseAsync(ConsoleChoiceRequest request, CancellationToken cancellationToken)
    {
        Request = request;
        return Task.FromResult(Answer);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        CancellableReads++;
        return Task.FromResult(Line);
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
        return Line;
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
    }
}
