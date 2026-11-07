using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>Writes a profile's switch to its fallback on the run protocol (llm.fallback).</summary>
internal sealed class RunEventsLlmFallbackObserver : ILlmFallbackObserver
{
    private readonly OrkeonEventWriter _events;

    public RunEventsLlmFallbackObserver(OrkeonEventWriter events) => _events = events;

    /// <inheritdoc />
    public void FallbackTaken(string profile, string fromProfile, string toProfile, string reason) =>
        _events.Emit(RunEventKinds.LlmFallback, new { profile, from = fromProfile, to = toProfile, reason });
}
