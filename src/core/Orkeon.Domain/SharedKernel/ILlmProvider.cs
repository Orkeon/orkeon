using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Domain interface for language model providers.
/// This is a pure domain interface without infrastructure concerns.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The configuration the provider was built with (model, key, base URL, timeout…) when it
    /// exposes one; default <see langword="null"/>. A configuration passed to <c>ChatAsync</c> or
    /// <c>GenerateAsync</c> <b>completes</b> this one (<see cref="LlmConfig.InheritFrom"/>, GAP-29):
    /// every field the call leaves unset is the provider's, every field it sets wins — a caller
    /// that names only a temperature keeps the provider's key, endpoint and timeout. A caller
    /// that wants the provider's sampling settings too (they have no unset value) starts from
    /// this configuration, as the chat client adapter and the scripting facade do.
    /// </summary>
    LlmConfig? BaseConfig => null;

    /// <summary>
    /// What this provider's API actually supports. Drives the translation of cross-cutting
    /// options (<c>response_format</c>, <c>thinking</c>, vision content) into the provider's
    /// own dialect, and lets an unsupported option be reported instead of silently dropped.
    /// </summary>
    /// <remarks>
    /// A default implementation, like <see cref="BaseConfig"/> above: third-party providers
    /// and test doubles keep compiling, and a provider that declares nothing is assumed to
    /// support nothing — nothing is written to the wire on its behalf.
    /// </remarks>
    LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Unknown;

    /// <summary>
    /// What this provider and <paramref name="model"/> take together for reasoning (#359): the effort levels,
    /// the thinking support and whether the capability table knows the model, by the rule the provider applies
    /// to every call. A decorator answers for the provider it wraps.
    /// </summary>
    /// <param name="model">The model to judge; null or blank for the provider's own.</param>
    /// <returns>The capabilities, or null when the provider cannot tell (a test double, a third-party provider).</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
    LlmReasoningCapabilities? DescribeReasoning(string? model) => null;

    /// <summary>
    /// The effort a call made with <paramref name="config"/> asks for and the one this provider sends (#360),
    /// judged as the provider judges the call itself: <paramref name="config"/> completed by the provider's own
    /// (<see cref="LlmConfig.InheritFrom"/>), on the model it resolves to. Nothing is logged.
    /// </summary>
    /// <param name="config">The call's configuration; null for the provider's own.</param>
    /// <returns>The report, or null when the call asks for no effort or the provider cannot tell.</returns>
    [System.Diagnostics.CodeAnalysis.Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
    LlmEffortReport? ReportEffort(LlmConfig? config) => null;

    /// <summary>
    /// Generates a response from the language model.
    /// </summary>
    Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chat interface for conversational interactions.
    /// </summary>
    Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);
}
