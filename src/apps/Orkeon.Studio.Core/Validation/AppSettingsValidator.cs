using Orkeon.Constants.Configuration;
using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Validation;

/// <summary>Why a document is being validated — it decides how hard some findings are.</summary>
public enum ValidationScope
{
    /// <summary>
    /// While the user is still editing: an empty mount list is a warning, because a launcher
    /// can still supply mounts on the command line.
    /// </summary>
    Editing,

    /// <summary>
    /// About to write the file: an empty mount list is an error. A settings file is meant to
    /// stand on its own (spec §4.5, "at least one mount"), and saving one that cannot start a
    /// run without extra command-line arguments is the mistake worth blocking.
    /// </summary>
    Saving,
}

/// <summary>
/// Pre-save validation of an <see cref="AppSettingsDocument"/>: well-formed JSON,
/// known keys holding values of a usable type, mounts the runtime will accept, and
/// the WIN-01 warning — the onboarding trap where a missing <c>Llm</c> section makes
/// the runtime degrade silently to the echo provider.
/// </summary>
public sealed class AppSettingsValidator
{
    /// <summary>
    /// The WIN-01 message, verbatim as <c>RunnerHost.LlmNotConfiguredMessage</c> words it so
    /// the UI warning and the runtime warning read the same.
    /// </summary>
    public const string LlmNotConfiguredMessage = OperatorMessages.LlmNotConfigured;

    /// <summary>
    /// Studio-only addendum to WIN-01. The runtime reads <c>ORKEON_Llm__*</c> environment
    /// variables as the <c>Llm</c> section and then warns about nothing; Studio only sees the
    /// file, so it must say that this finding can be a false alarm rather than let a user go
    /// hunting for a problem their environment already solved.
    /// </summary>
    public const string LlmNotConfiguredEnvironmentNote =
        "Studio inspects this file alone: if `ORKEON_Llm__BaseUrl` / `ORKEON_Llm__Model` are set " +
        "in the environment the run is launched with, the runtime reads them as the `Llm` " +
        "section and emits no warning.";

    /// <summary>Full text of the WIN-01 finding as reported by this validator.</summary>
    public const string LlmNotConfiguredWarning =
        LlmNotConfiguredMessage + " " + LlmNotConfiguredEnvironmentNote;

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
        McpSection.SectionPath + ":Enabled",
        ShellToolsSection.SectionPath + ":AllowInterpreters",
    ];

    // The words that make an environment value read as a secret: written in clear in the
    // settings file, it is the very thing the API-key rows keep out of every file.
    private static readonly string[] SecretWords = ["TOKEN", "SECRET", "KEY", "PASSWORD", "PASSWD"];

    // The indirection spellings a value may use to NAME a variable instead of holding one;
    // the same four the team catalog tolerates (STUDIO-16).
    private static readonly string[] IndirectionPrefixes = ["${", "%", "env:", "ORKEON_"];

    private readonly MountValidator _mounts;

    /// <summary>Creates a validator over the given directory access (defaults to the real disk).</summary>
    public AppSettingsValidator(IDirectoryProbe? directories = null) =>
        _mounts = new MountValidator(directories);

    /// <summary>Parses then validates JSON text; a malformed document yields a single error.</summary>
    /// <param name="json">The file's text.</param>
    /// <param name="scope">Why the document is being validated; see <see cref="ValidationScope"/>.</param>
    public IReadOnlyList<ValidationMessage> ValidateJson(
        string? json,
        ValidationScope scope = ValidationScope.Editing)
    {
        if (AppSettingsDocument.TryParse(json, out var document, out var error))
            return Validate(document, scope);

        return
        [
            ValidationMessage.Error(
                ValidationCodes.MalformedJson,
                string.Create(CultureInfo.InvariantCulture, $"The file is not valid JSON: {error}")),
        ];
    }

    /// <summary>Validates a document, most severe findings first within each family.</summary>
    /// <param name="document">The edited document.</param>
    /// <param name="scope">Why the document is being validated; see <see cref="ValidationScope"/>.</param>
    public IReadOnlyList<ValidationMessage> Validate(
        AppSettingsDocument document,
        ValidationScope scope = ValidationScope.Editing)
    {
        ArgumentNullException.ThrowIfNull(document);

        var messages = new List<ValidationMessage>();

        ValidateLlm(document, messages);
        ValidateTypes(document, messages);
        ValidateRagProfile(document, messages);
        ValidateMounts(document, messages, scope);
        ValidateMcp(document, messages);

        return messages;
    }

    /// <summary>
    /// The MCP servers (STUDIO-21): the rules the runtime applies when it connects, said
    /// before the file is written rather than at the next run. An identifier is a
    /// configuration key, so it must survive the binder; a stdio server without a command
    /// and an HTTP server without a URL are the two refusals <c>McpToolProvider</c> raises;
    /// a secret written in an environment block is the same clear-text trap as an inline
    /// API key, reported at the same level.
    /// </summary>
    private static void ValidateMcp(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        var serversNode = document.GetNode(McpSection.ServersPath);
        if (serversNode is not null and not JsonObject)
        {
            messages.Add(WrongType(McpSection.ServersPath, "an object of servers keyed by identifier"));
            return;
        }

        foreach (var id in document.Mcp.ServerIds)
        {
            var path = $"{McpSection.ServersPath}:{id}";
            if (!McpSection.IsValidServerId(id))
            {
                messages.Add(ValidationMessage.Error(
                    ValidationCodes.McpServerIdInvalid,
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{id}' is not a usable MCP server identifier: letters, digits, '.', '_' and '-' only."),
                    path));
            }

            if (document.Mcp.GetServer(id) is not { } server)
                continue;

            if (!server.IsStdio && !server.IsSse)
            {
                messages.Add(ValidationMessage.Error(
                    ValidationCodes.McpTransportUnknown,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Unknown MCP transport '{server.Transport}' for server '{id}'. Known transports: {string.Join(", ", McpSection.Transports)}."),
                    $"{path}:Transport"));
            }
            else if (server.IsStdio && string.IsNullOrWhiteSpace(server.Command))
            {
                messages.Add(ValidationMessage.Error(
                    ValidationCodes.McpCommandMissing,
                    string.Create(CultureInfo.InvariantCulture,
                        $"MCP server '{id}' uses the stdio transport and names no command to launch."),
                    $"{path}:Command"));
            }
            else if (server.IsSse
                && !(Uri.TryCreate(server.Url, UriKind.Absolute, out var url)
                    && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)))
            {
                messages.Add(ValidationMessage.Error(
                    ValidationCodes.McpUrlInvalid,
                    string.Create(CultureInfo.InvariantCulture,
                        $"MCP server '{id}' uses the HTTP (sse) transport and '{server.Url}' is not an absolute http(s) URL."),
                    $"{path}:Url"));
            }

            foreach (var (name, value) in server.Env)
            {
                if (LooksLikeSecret(name, value))
                {
                    messages.Add(ValidationMessage.Information(
                        ValidationCodes.McpEnvLooksSecret,
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{name}' of MCP server '{id}' is written in clear text in this file. The server process inherits your user environment: set the variable there and leave it out of the file."),
                        $"{path}:Env:{name}"));
                }
            }
        }
    }

    private static bool LooksLikeSecret(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (IndirectionPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)))
            return false;

        return SecretWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateLlm(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        if (!document.Llm.Exists)
        {
            messages.Add(ValidationMessage.Warning(
                ValidationCodes.LlmSectionMissing, LlmNotConfiguredWarning, LlmSection.SectionPath));
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
        // A node that parses as the declared kind is silent; an absent one is silent too --
        // only a value of the wrong shape is reported, one message per field.
        messages.AddRange(StringFields
            .Where(path => document.GetNode(path) is JsonObject or JsonArray)
            .Select(path => WrongType(path, "a string")));

        messages.AddRange(NumberFields
            .Where(path => document.GetNode(path) is not null && document.GetDouble(path) is null)
            .Select(path => WrongType(path, "a number")));

        messages.AddRange(BooleanFields
            .Where(path => document.GetNode(path) is not null && document.GetBoolean(path) is null)
            .Select(path => WrongType(path, "true or false")));

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

    private void ValidateMounts(
        AppSettingsDocument document,
        List<ValidationMessage> messages,
        ValidationScope scope)
    {
        var entries = document.Mounts.RawEntries;

        if (entries.Count == 0)
        {
            // While editing this is only a warning: the runner injects the crew's config
            // directory on its own and a launcher may add --mount arguments, so an empty array
            // is not automatically a dead configuration. Saving one is a different matter — a
            // settings file is expected to stand on its own, so the save path blocks.
            const string Text =
                "No file system mount is declared. The runtime refuses to start unless at " +
                "least one mount is configured here or passed at launch (--mount).";

            messages.Add(scope == ValidationScope.Saving
                ? ValidationMessage.Error(ValidationCodes.MountsEmpty, Text, MountsSection.SectionPath)
                : ValidationMessage.Warning(ValidationCodes.MountsEmpty, Text, MountsSection.SectionPath));
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
