using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>Writes the model calls on the run protocol (llm.call.*).</summary>
internal sealed class RunEventsLlmCallObserver : ILlmCallObserver
{
    private readonly OrkeonEventWriter _events;
    private readonly TimeProvider _time;

    public RunEventsLlmCallObserver(OrkeonEventWriter events, TimeProvider time)
    {
        _events = events;
        _time = time;
    }

    /// <summary>At most one progress line per call in this interval.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ProgressState> _calls = [];

    /// <inheritdoc />
    public void Started(string callId, LlmCallMode mode, int estimatedPromptTokens) =>
        _events.Emit(RunEventKinds.LlmCallStarted, new { callId, mode = Wire(mode), estimatedPromptTokens });

    /// <inheritdoc />
    public void Progress(string callId, int estimatedCompletionTokens)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (_calls.TryGetValue(callId, out var state) && _time.GetElapsedTime(state.WrittenAt, now) < ProgressInterval)
            {
                _calls[callId] = state with { Swallowed = estimatedCompletionTokens };
                return;
            }

            _calls[callId] = new ProgressState(now, null);
            EmitProgress(callId, estimatedCompletionTokens);
        }
    }

    /// <inheritdoc />
    public void Finished(string callId, LlmCallMode mode, bool fellBack, int? promptTokens, int? completionTokens)
    {
        lock (_gate)
        {
            if (_calls.Remove(callId, out var state) && state.Swallowed is { } last)
                EmitProgress(callId, last);
            _events.Emit(RunEventKinds.LlmCallFinished, new { callId, mode = Wire(mode), fellBack, promptTokens, completionTokens });
        }
    }

    private void EmitProgress(string callId, int estimatedCompletionTokens) =>
        _events.Emit(RunEventKinds.LlmCallProgress, new { callId, estimatedCompletionTokens });

    private static string Wire(LlmCallMode mode) => mode == LlmCallMode.Stream ? "stream" : "buffer";

    private readonly record struct ProgressState(long WrittenAt, int? Swallowed);
}
