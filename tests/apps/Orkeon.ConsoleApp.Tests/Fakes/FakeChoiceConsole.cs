using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>A console with panels: answers what it is configured to and records what it is asked.</summary>
internal sealed class FakeChoiceConsole : IConsoleAdapter, IChoiceConsole
{
    public bool CanChoose { get; set; } = true;

    public ConsoleChoiceAnswer? Answer { get; set; }

    public ConsoleChoiceRequest? Request { get; private set; }

    /// <summary>The reads made for a script's own question.</summary>
    public int AnswerReads { get; private set; }

    public Task<string?> ReadAnswerAsync(CancellationToken cancellationToken)
    {
        AnswerReads++;
        return Task.FromResult<string?>(null);
    }

    public Task<ConsoleChoiceAnswer?> ChooseAsync(ConsoleChoiceRequest request, CancellationToken cancellationToken)
    {
        Request = request;
        return Task.FromResult(Answer);
    }

    public void Write(string text)
    {
    }

    public void WriteLine(string text)
    {
    }

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
    }
}
