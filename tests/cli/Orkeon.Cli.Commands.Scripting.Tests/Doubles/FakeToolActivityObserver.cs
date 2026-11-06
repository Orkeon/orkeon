using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary><see cref="IToolActivityObserver"/> recording what it hears, in order.</summary>
internal sealed class FakeToolActivityObserver : IToolActivityObserver
{
    public List<string> Seen { get; } = [];

    public List<long> StartedIds { get; } = [];

    public ToolCallFinished? LastFinished { get; private set; }

    public bool ThrowOnStarted { get; set; }

    public bool ThrowOnFinished { get; set; }

    public void Started(ToolCallStarted toolCall)
    {
        if (ThrowOnStarted) throw new InvalidOperationException("started");
        Seen.Add("started");
        StartedIds.Add(toolCall.Id);
    }

    public void Finished(ToolCallFinished toolCall)
    {
        if (ThrowOnFinished) throw new InvalidOperationException("finished");
        Seen.Add($"finished:{toolCall.Succeeded}");
        LastFinished = toolCall;
    }
}
