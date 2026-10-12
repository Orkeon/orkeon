using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Records every notification of <see cref="ILlmCallObserver"/>, in order.</summary>
public sealed class RecordingLlmCallObserver : ILlmCallObserver
{
    public sealed record StartedNote(string CallId, LlmCallMode Mode, int EstimatedPromptTokens);

    public sealed record ProgressNote(string CallId, int EstimatedCompletionTokens);

    public sealed record FinishedNote(string CallId, LlmCallMode Mode, bool FellBack, int? PromptTokens, int? CompletionTokens);

    public List<StartedNote> StartedCalls { get; } = [];

    public List<ProgressNote> ProgressCalls { get; } = [];

    public List<FinishedNote> FinishedCalls { get; } = [];

    public void Started(string callId, LlmCallMode mode, int estimatedPromptTokens) =>
        StartedCalls.Add(new StartedNote(callId, mode, estimatedPromptTokens));

    public void Progress(string callId, int estimatedCompletionTokens) =>
        ProgressCalls.Add(new ProgressNote(callId, estimatedCompletionTokens));

    public void Finished(string callId, LlmCallMode mode, bool fellBack, int? promptTokens, int? completionTokens) =>
        FinishedCalls.Add(new FinishedNote(callId, mode, fellBack, promptTokens, completionTokens));
}
