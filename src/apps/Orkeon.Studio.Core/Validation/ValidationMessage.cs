namespace Orkeon.Studio.Core.Validation;

/// <summary>How a <see cref="ValidationMessage"/> should be surfaced by a UI.</summary>
public enum ValidationSeverity
{
    /// <summary>Advice; nothing is wrong.</summary>
    Information,

    /// <summary>The configuration is usable but will behave in a way the user may not expect.</summary>
    Warning,

    /// <summary>The configuration would be rejected at runtime.</summary>
    Error,
}

/// <summary>
/// One validation finding. UI-agnostic on purpose: the three Studio front-ends render
/// the same list, keyed by <see cref="Code"/>.
/// </summary>
public sealed record ValidationMessage
{
    /// <summary>Severity of the finding.</summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>Stable identifier (see <see cref="ValidationCodes"/>), for UI keying and tests.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Text { get; init; }

    /// <summary>Configuration path or mount entry the finding is about, when applicable.</summary>
    public string? Path { get; init; }

    /// <summary>Creates an informational message.</summary>
    public static ValidationMessage Information(string code, string text, string? path = null) =>
        new() { Severity = ValidationSeverity.Information, Code = code, Text = text, Path = path };

    /// <summary>Creates a warning message.</summary>
    public static ValidationMessage Warning(string code, string text, string? path = null) =>
        new() { Severity = ValidationSeverity.Warning, Code = code, Text = text, Path = path };

    /// <summary>Creates an error message.</summary>
    public static ValidationMessage Error(string code, string text, string? path = null) =>
        new() { Severity = ValidationSeverity.Error, Code = code, Text = text, Path = path };
}

/// <summary>Stable codes carried by <see cref="ValidationMessage.Code"/>.</summary>
public static class ValidationCodes
{
    /// <summary>
    /// No <c>Llm</c> section: the runtime degrades silently to the echo provider.
    /// Named after the onboarding trap the runtime warning itself is tracked under.
    /// </summary>
    public const string LlmSectionMissing = "WIN-01";

    /// <summary>The document is not well-formed JSON.</summary>
    public const string MalformedJson = "STUDIO-JSON";

    /// <summary>A known key holds a value of the wrong JSON type.</summary>
    public const string InvalidFieldType = "STUDIO-TYPE";

    /// <summary>The API key is stored in clear text instead of an environment variable.</summary>
    public const string InlineApiKey = "STUDIO-LLM-APIKEY";

    /// <summary><c>Orkeon:Rag:Profile</c> names no known profile.</summary>
    public const string UnknownRagProfile = "STUDIO-RAG-PROFILE";

    /// <summary>No mount is declared.</summary>
    public const string MountsEmpty = "STUDIO-MOUNT-EMPTY";

    /// <summary>A mount entry does not parse.</summary>
    public const string MountFormat = "STUDIO-MOUNT-FORMAT";

    /// <summary>A mount's physical path does not exist on disk.</summary>
    public const string MountPathMissing = "STUDIO-MOUNT-PATH";

    /// <summary>Two mounts claim the same virtual path.</summary>
    public const string MountVirtualCollision = "STUDIO-MOUNT-COLLISION";
}
