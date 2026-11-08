using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Testing;

/// <summary>
/// The provider an <c>orkeon run --test</c> script runs on: it wraps the host's configured provider,
/// and an expectation registered with <c>test.mockLlm</c> answers before the profile is reached. A
/// prompt no expectation fits goes to the configured provider (or, when the host has none, is
/// answered empty). Every call is recorded for <c>test.assertLlmCalled</c>.
/// </summary>
public sealed class TestOverrideLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private readonly ILlmProvider? _inner;

    /// <summary>Creates the override over <paramref name="inner"/>, the provider the host configured.</summary>
    /// <param name="inner">The configured provider; null when the host has none.</param>
    public TestOverrideLlmProvider(ILlmProvider? inner)
    {
        _inner = inner;
        Mock = new MockLlmProvider();
    }

    /// <summary>
    /// Creates an override over <paramref name="inner"/> that shares <paramref name="mock"/> with another one:
    /// the provider of a profile the script picks answers from, and is counted in, the same expectations.
    /// </summary>
    /// <param name="inner">The profile's provider.</param>
    /// <param name="mock">The mock the script's expectations are registered on.</param>
    public TestOverrideLlmProvider(ILlmProvider? inner, MockLlmProvider mock)
    {
        ArgumentNullException.ThrowIfNull(mock);
        _inner = inner;
        Mock = mock;
    }

    /// <summary>The mock the <c>test</c> namespace registers its expectations on and asserts against.</summary>
    public MockLlmProvider Mock { get; }

    /// <inheritdoc />
    public string Name => _inner?.Name ?? Mock.Name;

    /// <inheritdoc />
    public LlmConfig? BaseConfig => _inner?.BaseConfig;

    /// <inheritdoc />
    public LlmProviderCapabilities Capabilities => _inner?.Capabilities ?? LlmProviderCapabilities.Unknown;

    private IStreamingLlmProvider? InnerStreaming => _inner is IStreamingLlmProvider { SupportsStreaming: true } streaming ? streaming : null;

    /// <summary>Whether the configured provider streams: the override streams what it forwards, and nothing else.</summary>
    public bool SupportsStreaming => InnerStreaming is not null;

    /// <summary>Whether the configured provider streams on the wire.</summary>
    public bool StreamsNatively => InnerStreaming?.StreamsNatively ?? false;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Mock.RecordGenerate(prompt);
        if (Mock.Match(prompt) is { } served)
        {
            yield return served.Content;
            yield break;
        }

        if (InnerStreaming is { } streaming)
        {
            await foreach (var chunk in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
                yield return chunk;
            yield break;
        }

        yield return _inner is null
            ? string.Empty
            : (await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false)).Content;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        Mock.RecordChat(messages);
        if (MockLlmProvider.PromptOf(messages) is { } prompt && Mock.Match(prompt) is { } served)
        {
            yield return LlmStreamEvent.Content(served.Content);
            yield return LlmStreamEvent.Complete(served);
            yield break;
        }

        if (InnerStreaming is { } streaming)
        {
            await foreach (var e in streaming.ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
                yield return e;
            yield break;
        }

        var response = _inner is null
            ? new LlmResponse { Content = string.Empty }
            : await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (response.Content.Length > 0)
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Mock.RecordGenerate(prompt);
        if (Mock.Match(prompt) is { } served)
            return Task.FromResult(served);

        return _inner is null
            ? Task.FromResult(new LlmResponse { Content = string.Empty })
            : _inner.GenerateAsync(prompt, config, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        Mock.RecordChat(messages);
        if (MockLlmProvider.PromptOf(messages) is { } prompt && Mock.Match(prompt) is { } served)
            return Task.FromResult(served);

        return _inner is null
            ? Task.FromResult(new LlmResponse { Content = string.Empty })
            : _inner.ChatAsync(messages, config, cancellationToken);
    }
}
