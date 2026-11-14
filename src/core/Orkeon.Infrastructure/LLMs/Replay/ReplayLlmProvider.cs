using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// Answers every call from a cassette. Its inner provider says what it is (name, capabilities,
/// streaming) and judges the request as a live call would (an audio part for a model that takes none
/// throws), but never sends it: a call the cassette does not hold fails with a
/// <see cref="ReplayMissException"/>, it does not reach the network. A call recorded as a failure fails
/// again with the same kind of exception.
/// </summary>
internal sealed class ReplayLlmProvider(ILlmProvider inner, LlmCassette cassette, string profile)
    : ILlmProvider, IStreamingLlmProvider, ILlmProviderDecorator
{
    public ILlmProvider Inner => inner;

    public string Name => inner.Name;

    public LlmConfig? BaseConfig => inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => inner.Capabilities;

    /// <inheritdoc/>
    public LlmReasoningCapabilities? DescribeReasoning(string? model) => inner.DescribeReasoning(model);

    /// <inheritdoc/>
    public LlmEffortReport? ReportEffort(LlmConfig? config) => inner.ReportEffort(config);

    public bool SupportsStreaming => (inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => (inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Validate(prompt, config);
        var taken = await cassette.TakeAsync(profile, LlmCallKey.OfPrompt(profile, prompt, config), Excerpt(prompt), cancellationToken).ConfigureAwait(false);
        return taken.Failure is { } failure ? throw failure : taken.Response;
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Validate(messages, config);
        var taken = await cassette.TakeAsync(profile, LlmCallKey.Of(profile, messages, config), LlmCallKey.Excerpt(messages), cancellationToken).ConfigureAwait(false);
        return taken.Failure is { } failure ? throw failure : taken.Response;
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(prompt, config);
        var taken = await cassette.TakeAsync(profile, LlmCallKey.OfPrompt(profile, prompt, config), Excerpt(prompt), cancellationToken).ConfigureAwait(false);
        if (taken.Events is null)
        {
            if (taken.Failure is { } early)
                throw early;
            if (taken.Response.Error is { } error)
                throw new InvalidOperationException(error);
            if (taken.Response.Content.Length > 0)
                yield return taken.Response.Content;
            yield break;
        }

        foreach (var ev in taken.Events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta && e.Delta is not null))
            yield return ev.Delta!;
        if (taken.Failure is { } failure)
            throw failure;
        if (taken.Response.Error is { } recorded)
            throw new InvalidOperationException(recorded);
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(messages, config);

        var taken = await cassette.TakeAsync(profile, LlmCallKey.Of(profile, messages, config), LlmCallKey.Excerpt(messages), cancellationToken).ConfigureAwait(false);
        if (taken.Events is null)
        {
            if (taken.Failure is { } early)
                throw early;
            if (taken.Response.Error is null && taken.Response.Content.Length > 0)
                yield return LlmStreamEvent.Content(taken.Response.Content);
        }
        else
        {
            foreach (var ev in taken.Events)
                yield return ev;
            if (taken.Failure is { } failure)
                throw failure;
        }

        yield return LlmStreamEvent.Complete(taken.Response);
    }

    /// <summary>The refusal a live call meets before sending anything (audio for a model that takes none).</summary>
    private void Validate(LlmMessage[] messages, LlmConfig? config)
    {
        if (inner is OpenAICompatibleProviderBase compatible)
            compatible.ValidateChatRequest(messages, config);
    }

    /// <summary>The same refusal for a plain prompt, judged as the one user message a live call sends.</summary>
    private void Validate(string prompt, LlmConfig? config) => Validate([UserMessage(prompt)], config);

    private static LlmMessage UserMessage(string prompt) => new() { Role = "user", Content = prompt ?? string.Empty };

    private static string Excerpt(string prompt) => LlmCallKey.Excerpt([UserMessage(prompt)]);
}
