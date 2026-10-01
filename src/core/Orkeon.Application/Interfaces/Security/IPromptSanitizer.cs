using Orkeon.Domain.Security;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Interface for sanitizing prompts and user data against injection attacks.
/// </summary>
public interface IPromptSanitizer
{
    /// <summary>
    /// Sanitizes the given input text according to the configured policy and context.
    /// </summary>
    /// <param name="input">The text to sanitize.</param>
    /// <param name="context">Context about the source and trust level of the input.</param>
    /// <returns>The sanitization result with any detected threats.</returns>
    SanitizationResult Sanitize(string input, SanitizationContext context);

    /// <summary>
    /// Detects the injection patterns in <paramref name="input"/>, whatever the configured
    /// policy — for callers that apply a policy of their own (the tool-result sanitizer).
    /// </summary>
    /// <param name="input">The text to scan.</param>
    /// <returns>The threats found, in no particular order; empty when none.</returns>
    IReadOnlyList<ThreatDetection> Detect(string input);

    /// <summary>
    /// Wraps user data with context delimiters to prevent injection via data boundaries.
    /// </summary>
    /// <param name="data">The user data to wrap.</param>
    /// <param name="sectionName">The name of the data section.</param>
    /// <returns>The wrapped data string.</returns>
    string WrapUserData(string data, string sectionName);
}
