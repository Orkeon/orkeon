using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// What a provider and one of its models take together for reasoning, by the rule every provider
/// applies to a call (#356, #359): the provider's declared <see cref="LlmProviderCapabilities"/> narrowed by
/// the model's entry of the capability table.
/// </summary>
/// <param name="Model">The model judged: the one named, else the provider's own.</param>
/// <param name="Known">
/// Whether the capability table has an entry for the model. On an unknown model the provider's levels rule
/// and an effort is sent unchecked.
/// </param>
/// <param name="Thinking">The lower of the provider's thinking support and the model's.</param>
/// <param name="Efforts">
/// The levels of <see cref="LlmThinkingConfig.Levels"/> a call may ask for and see sent as asked, in order:
/// the provider's levels narrowed by the model's list. Empty when the model takes no effort, has no reasoning
/// pass, or the provider carries none of its levels. A vendor value (<c>none</c>, <c>minimal</c>) is no level
/// and is not listed.
/// </param>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record LlmReasoningCapabilities(
    string Model,
    bool Known,
    ThinkingSupport Thinking,
    IReadOnlyList<string> Efforts);

/// <summary>What the runtime did with the effort a call asked for (#360), when it did not send it as asked.</summary>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public enum LlmEffortNotice
{
    /// <summary>The level is not one the provider and the model both take: the nearest one below was sent, or the lowest one when none lies below.</summary>
    Lowered,

    /// <summary>No effort was sent: the model takes none, the model or the provider has no thinking control, or the provider's field carries none of the model's levels.</summary>
    Dropped,

    /// <summary>The model is in no capability table: the effort was sent as asked, unchecked.</summary>
    Unchecked,
}

/// <summary>
/// The effort a call asked for and the one its provider sends (#360): the same judgement the provider applies
/// just before the request leaves, the host's <c>Llm:Thinking:Effort</c> included.
/// </summary>
/// <param name="Model">The model the call goes to.</param>
/// <param name="Asked">The effort asked, in its canonical spelling.</param>
/// <param name="Sent">The effort sent, or null when none is.</param>
/// <param name="Notice">Why <paramref name="Sent"/> differs from <paramref name="Asked"/>, or could not be checked; null when it is sent as asked on a model the table knows.</param>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record LlmEffortReport(
    string Model,
    string Asked,
    string? Sent,
    LlmEffortNotice? Notice);
