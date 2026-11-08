using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.LLMs.Capabilities;

/// <summary>
/// What one model accepts and honours, as opposed to <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmProviderCapabilities"/>,
/// which a provider declares for every model it serves (FWK-21).
/// </summary>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record ModelCapabilities
{
    /// <summary>Whether the model takes audio content parts as input.</summary>
    public bool AudioInput { get; init; }

    /// <summary>Whether the model answers in audio. Recorded for the table; no code path sends or reads audio output.</summary>
    public bool AudioOutput { get; init; }

    /// <summary>Whether the model takes image content parts as input.</summary>
    public bool Vision { get; init; }

    /// <summary>
    /// The reasoning effort levels the model accepts: an empty list when it takes none, null when the table
    /// does not restrict them (the provider's own declared levels then rule).
    /// </summary>
    public IReadOnlyList<string>? ReasoningEfforts { get; init; }
}

/// <summary>
/// A host's entry of <c>Llm:ModelCapabilities:&lt;id&gt;</c>: a field left null keeps the built-in value of the
/// model, so an override that sets <c>AudioInput</c> alone keeps the built-in effort restriction.
/// </summary>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record ModelCapabilityOverride
{
    /// <summary>Audio input, or null to keep the built-in value.</summary>
    public bool? AudioInput { get; init; }

    /// <summary>Audio output, or null to keep the built-in value.</summary>
    public bool? AudioOutput { get; init; }

    /// <summary>Image input, or null to keep the built-in value.</summary>
    public bool? Vision { get; init; }

    /// <summary>The efforts the model takes (empty: none), or null to keep the built-in value.</summary>
    public IReadOnlyList<string>? ReasoningEfforts { get; init; }
}

/// <summary>Answers what a model id can do: the table the providers and the profile registry read (FWK-21).</summary>
[Experimental("ORKEXP005", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IModelCapabilityRegistry
{
    /// <summary>
    /// The capabilities of the entry whose id is the longest prefix of <paramref name="model"/>, case-insensitive;
    /// null when the table knows no such model.
    /// </summary>
    /// <param name="model">The model id as the provider sends it.</param>
    ModelCapabilities? TryGet(string? model);
}
