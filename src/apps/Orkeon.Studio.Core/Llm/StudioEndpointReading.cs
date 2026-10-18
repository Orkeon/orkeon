using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Studio.Core.Llm;

/// <summary>Reads an address through <see cref="LlmEndpointReading"/>, as the run does; a blank one deduces nothing.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054",
    Justification = "The input is the raw 'Llm:BaseUrl' field, possibly half-typed; the reading refuses what does not parse.")]
public static class StudioEndpointReading
{
    /// <summary>The reading of an address, or null when it is blank (no provider is deduced, never the model or key fallback).</summary>
    public static LlmEndpointReading? Of(string? baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : LlmEndpointReading.Of(baseUrl, null, null);

    /// <summary>The provider key the address says; <c>none</c> when it is blank.</summary>
    public static string ProviderOf(string? baseUrl) =>
        Of(baseUrl)?.ProviderKey ?? LlmProviderKeys.None;
}
