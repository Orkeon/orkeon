namespace Orkeon.Domain.Security;

/// <summary>
/// Result of sanitizing a prompt or text input for injection threats.
/// </summary>
public record SanitizationResult
{
    /// <summary>Gets the sanitized text.</summary>
    public string SanitizedText { get; init; } = string.Empty;
    /// <summary>Gets whether the input was blocked entirely.</summary>
    public bool IsBlocked { get; init; }
    /// <summary>Gets the list of detected threats.</summary>
    public IReadOnlyList<ThreatDetection> Threats { get; init; } = Array.Empty<ThreatDetection>();

    /// <summary>Creates a clean (no threats) sanitization result.</summary>
    /// <param name="text">The clean text.</param>
    /// <returns>A <see cref="SanitizationResult"/> with no threats.</returns>
    public static SanitizationResult Clean(string text) =>
        new() { SanitizedText = text, IsBlocked = false };

    /// <summary>Creates a sanitization result with warnings but no blocking.</summary>
    /// <param name="text">The (possibly modified) text.</param>
    /// <param name="threats">The detected threats.</param>
    /// <returns>A <see cref="SanitizationResult"/> with warnings.</returns>
    public static SanitizationResult WithWarnings(string text, IReadOnlyList<ThreatDetection> threats) =>
        new() { SanitizedText = text, IsBlocked = false, Threats = threats };

    /// <summary>Creates a sanitization result with threats stripped from the text.</summary>
    /// <param name="text">The stripped text.</param>
    /// <param name="threats">The detected threats that were stripped.</param>
    /// <returns>A <see cref="SanitizationResult"/> with stripped content.</returns>
    public static SanitizationResult Stripped(string text, IReadOnlyList<ThreatDetection> threats) =>
        new() { SanitizedText = text, IsBlocked = false, Threats = threats };

    /// <summary>Creates a blocked sanitization result.</summary>
    /// <param name="threats">The threats that caused blocking.</param>
    /// <returns>A blocked <see cref="SanitizationResult"/>.</returns>
    public static SanitizationResult Blocked(IReadOnlyList<ThreatDetection> threats) =>
        new() { SanitizedText = string.Empty, IsBlocked = true, Threats = threats };
}

/// <summary>
/// Represents a detected threat in the input text.
/// </summary>
/// <param name="Type">The threat type.</param>
/// <param name="Pattern">The pattern that matched.</param>
/// <param name="MatchedText">The matched text.</param>
/// <param name="Position">The position in the text where the match was found.</param>
/// <param name="Severity">The severity of the detected threat.</param>
public record ThreatDetection(
    ThreatType Type,
    string Pattern,
    string MatchedText,
    int Position,
    ThreatSeverity Severity);

/// <summary>
/// Context for sanitization, providing information about the source of the input.
/// </summary>
/// <param name="Source">The source of the input.</param>
/// <param name="AgentRole">The role of the agent providing input.</param>
/// <param name="IsTrusted">Whether the source is trusted.</param>
public record SanitizationContext(
    string Source,
    string AgentRole,
    bool IsTrusted);

/// <summary>
/// Types of prompt injection threats.
/// </summary>
public enum ThreatType
{
    /// <summary>A prompt injection attack.</summary>
    PromptInjection,
    /// <summary>A jailbreak attempt.</summary>
    Jailbreak,
    /// <summary>Data exfiltration attempt.</summary>
    DataExfiltration,
    /// <summary>Token manipulation attack.</summary>
    TokenManipulation
}

/// <summary>
/// Severity levels for detected threats.
/// </summary>
public enum ThreatSeverity
{
    /// <summary>Low severity threat.</summary>
    Low,
    /// <summary>Medium severity threat.</summary>
    Medium,
    /// <summary>High severity threat.</summary>
    High,
    /// <summary>Critical severity threat.</summary>
    Critical
}

/// <summary>
/// Policy for handling detected threats.
/// </summary>
public enum SanitizationPolicy
{
    /// <summary>No action taken.</summary>
    None,
    /// <summary>Warn but allow.</summary>
    Warn,
    /// <summary>Strip the threat from input.</summary>
    Strip,
    /// <summary>Block the input entirely.</summary>
    Block
}
