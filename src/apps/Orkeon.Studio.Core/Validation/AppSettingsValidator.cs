using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Validation;

/// <summary>
/// Pre-save validation of an <see cref="AppSettingsDocument"/>: well-formed JSON,
/// known keys holding values of a usable type, mounts the runtime will accept, and
/// the WIN-01 warning — the onboarding trap where a missing <c>Llm</c> section makes
/// the runtime degrade silently to the echo provider.
/// </summary>
public sealed class AppSettingsValidator
{
    /// <summary>
    /// The WIN-01 message, worded as <c>RunnerHost.WarnIfLlmNotConfigured</c> words it
    /// so the UI warning and the runtime warning read the same.
    /// </summary>
    public const string LlmNotConfiguredMessage =
        "No `Llm` section configured — falling back to the echo provider (`<undefined-llm>`). " +
        "Run `orkeon init` to create a configuration, or set `ORKEON_Llm__BaseUrl` / `ORKEON_Llm__Model`.";

    private static readonly string[] StringFields =
    [
        "Llm:Model",
        "Llm:BaseUrl",
        "Llm:ApiKey",
        "Orkeon:Rag:Profile",
        "Orkeon:Rag:Provider",
        "Orkeon:Rag:ConnectionString",
    ];

    private static readonly string[] NumberFields =
    [
        "Llm:Temperature",
        "Llm:MaxTokens",
        "Llm:TimeoutSeconds",
        "RateLimiting:MaxConcurrentRequests",
        "RateLimiting:GlobalRequestsPerMinute",
        "RateLimiting:ProviderRequestsPerMinute",
        "RateLimiting:AgentRequestsPerMinute",
        "RateLimiting:QueueLimit",
        "LlmLogging:MaxBodyLengthChars",
        "Orkeon:Rag:Corrective:MaxIterations",
    ];

    private static readonly string[] BooleanFields =
    [
        "LlmLogging:FullEmbeddingLog",
        "LlmLogging:LogStreamingExchanges",
        "Orkeon:Rag:Retrieval:Hybrid:Enabled",
        "Orkeon:Rag:Corrective:WebFallback:Enabled",
        "Orkeon:Rag:WebFallback:Enabled",
    ];

    private readonly MountValidator _mounts;

    /// <summary>Creates a validator over the given directory access (defaults to the real disk).</summary>
    public AppSettingsValidator(IDirectoryProbe? directories = null) =>
        _mounts = new MountValidator(directories);

    /// <summary>Parses then validates JSON text; a malformed document yields a single error.</summary>
    public IReadOnlyList<ValidationMessage> ValidateJson(string? json)
    {
        if (AppSettingsDocument.TryParse(json, out var document, out var error))
            return Validate(document);

        return
        [
            ValidationMessage.Error(
                ValidationCodes.MalformedJson,
                string.Create(CultureInfo.InvariantCulture, $"The file is not valid JSON: {error}")),
        ];
    }

    /// <summary>Validates a document, most severe findings first within each family.</summary>
    public IReadOnlyList<ValidationMessage> Validate(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var messages = new List<ValidationMessage>();

        ValidateLlm(document, messages);
        ValidateTypes(document, messages);
        ValidateRagProfile(document, messages);
        ValidateMounts(document, messages);

        return messages;
    }

    private static void ValidateLlm(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        if (!document.Llm.Exists)
        {
            messages.Add(ValidationMessage.Warning(
                ValidationCodes.LlmSectionMissing, LlmNotConfiguredMessage, LlmSection.SectionPath));
            return;
        }

        // "localhost:11434" parses as an absolute URI whose *scheme* is "localhost", so
        // the scheme check is what actually catches a URL missing its http:// prefix.
        if (document.Llm.BaseUrl is { Length: > 0 } baseUrl
            && !(Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            messages.Add(ValidationMessage.Error(
                ValidationCodes.InvalidFieldType,
                string.Create(CultureInfo.InvariantCulture, $"'{baseUrl}' is not an absolute http(s) URL."),
                "Llm:BaseUrl"));
        }

        if (document.Llm.ApiKey is { Length: > 0 } apiKey
            && !string.Equals(apiKey, LlmPresets.DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal))
        {
            messages.Add(ValidationMessage.Information(
                ValidationCodes.InlineApiKey,
                "The API key is stored in clear text in this file. Prefer the " +
                $"{LlmPresets.DefaultApiKeyEnv} environment variable, which the runtime reads " +
                "with precedence over the file.",
                "Llm:ApiKey"));
        }
    }

    private static void ValidateTypes(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        foreach (var path in StringFields)
        {
            if (document.GetNode(path) is JsonObject or JsonArray)
                messages.Add(WrongType(path, "a string"));
        }

        foreach (var path in NumberFields)
        {
            var node = document.GetNode(path);
            if (node is not null && document.GetDouble(path) is null)
                messages.Add(WrongType(path, "a number"));
        }

        foreach (var path in BooleanFields)
        {
            var node = document.GetNode(path);
            if (node is not null && document.GetBoolean(path) is null)
                messages.Add(WrongType(path, "true or false"));
        }

        var mountsNode = document.GetNode(MountsSection.SectionPath);
        if (mountsNode is not null and not JsonArray)
            messages.Add(WrongType(MountsSection.SectionPath, "an array of mount strings"));

        var logLevelsNode = document.GetNode(LoggingSection.SectionPath);
        if (logLevelsNode is not null and not JsonObject)
            messages.Add(WrongType(LoggingSection.SectionPath, "an object mapping categories to levels"));
    }

    private static void ValidateRagProfile(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        if (document.Rag.HasValidProfile)
            return;

        messages.Add(ValidationMessage.Error(
            ValidationCodes.UnknownRagProfile,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Unknown RAG profile '{document.Rag.Profile}'. Known profiles: " +
                $"{string.Join(", ", RagSection.KnownProfiles)}."),
            $"{RagSection.SectionPath}:Profile"));
    }

    private void ValidateMounts(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        var entries = document.Mounts.RawEntries;

        if (entries.Count == 0)
        {
            // Not an error on its own: a launcher may inject mounts on the command line.
            // It is fatal only if nothing else declares one, hence the warning.
            messages.Add(ValidationMessage.Warning(
                ValidationCodes.MountsEmpty,
                "No file system mount is declared. The runtime refuses to start unless at " +
                "least one mount is configured here or passed at launch (--mount).",
                MountsSection.SectionPath));
            return;
        }

        messages.AddRange(_mounts.Validate(entries, requireAtLeastOne: false));
    }

    private static ValidationMessage WrongType(string path, string expected) =>
        ValidationMessage.Error(
            ValidationCodes.InvalidFieldType,
            string.Create(CultureInfo.InvariantCulture, $"'{path}' must be {expected}."),
            path);
}
