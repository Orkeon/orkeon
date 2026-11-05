using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary><see cref="IToolActivityObserver"/> recording what it hears, in order.</summary>
internal sealed class FakeToolActivityObserver : IToolActivityObserver
{
    public List<string> Seen { get; } = [];

    public List<long> StartedIds { get; } = [];

    public void Started(ToolCallStarted toolCall)
    {
        Seen.Add("started");
        StartedIds.Add(toolCall.Id);
    }

    public void Finished(ToolCallFinished toolCall) => Seen.Add($"finished:{toolCall.Succeeded}");
}
