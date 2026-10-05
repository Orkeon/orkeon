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
        "Orkeon:Rag:Profile",
        "Orkeon:Rag:Provider",
        RagSection.LlmProfilePath,
    ];

    // Every field the run reads as an integer (int.TryParse, or the binder): 600.0, 0.5 or "1e3" is
    // no integer to it — ignored, or a refused start (STUDIO-55). The Llm shape's own are below.
    private static readonly string[] IntegerFields =
    [
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

    // The keys of a section of the Llm shape — Llm itself and each entry of Llm:Profiles —, by the
    // kind the run reads them as (LlmSettings): relative to the section.
    private static readonly string[] LlmStringKeys = ["Model", "BaseUrl", "ApiKey", ConfigurationKeys.LlmApiKeyEnvVar, "Thinking:Effort"];
    private static readonly string[] LlmIntegerKeys = ["MaxTokens", "TimeoutSeconds", "MaxRetries"];
    private static readonly string[] LlmBooleanKeys = ["Thinking:Enabled", ConfigurationKeys.LlmGrammar];

    // The words that make an environment value read as a secret: written in clear in the
    // settings file, it is the very thing the API-key rows keep out of every file.
    private static readonly string[] SecretWords = ["TOKEN", "SECRET", "KEY", "PASSWORD", "PASSWD"];

    // The indirection spellings a value may use to NAME a variable instead of holding one —
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
        ValidateRagLlmProfile(document, messages);
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
            ValidateMcpServer(document, id, messages);
    }

    private static void ValidateMcpServer(AppSettingsDocument document, string id, List<ValidationMessage> messages)
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
            return;

        if (TransportError(server, id, path) is { } transportError)
            messages.Add(transportError);

        foreach (var (name, value) in server.Env.Where(entry => LooksLikeSecret(entry.Key, entry.Value)))
        {
            messages.Add(ValidationMessage.Information(
                ValidationCodes.McpEnvLooksSecret,
                string.Create(CultureInfo.InvariantCulture,
                    $"'{name}' of MCP server '{id}' is written in clear text in this file. The server process inherits your user environment: set the variable there and leave it out of the file."),
                $"{path}:Env:{name}"));
        }
    }

    /// <summary>
    /// The one refusal a server's transport earns: unknown, stdio without a command, or HTTP
    /// (sse) without an absolute http(s) URL. Null when the transport is complete.
    /// </summary>
    private static ValidationMessage? TransportError(McpServerDefinition server, string id, string path)
    {
        if (!server.IsStdio && !server.IsSse)
        {
            return ValidationMessage.Error(
                ValidationCodes.McpTransportUnknown,
                string.Create(CultureInfo.InvariantCulture,
                    $"Unknown MCP transport '{server.Transport}' for server '{id}'. Known transports: {string.Join(", ", McpSection.Transports)}."),
                $"{path}:Transport");
        }

        if (server.IsStdio && string.IsNullOrWhiteSpace(server.Command))
        {
            return ValidationMessage.Error(
                ValidationCodes.McpCommandMissing,
                string.Create(CultureInfo.InvariantCulture,
                    $"MCP server '{id}' uses the stdio transport and names no command to launch."),
                $"{path}:Command");
        }

        if (server.IsSse && !LlmSection.IsAbsoluteHttpUrl(server.Url))
        {
            return ValidationMessage.Error(
                ValidationCodes.McpUrlInvalid,
                string.Create(CultureInfo.InvariantCulture,
                    $"MCP server '{id}' uses the HTTP (sse) transport and '{server.Url}' is not an absolute http(s) URL."),
                $"{path}:Url");
        }

        return null;
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
        }

        if (document.GetNode(LlmSection.SectionPath) is not JsonObject section)
            return;

        // STUDIO-55: the default is read as strictly as a profile at start (GAP-40) — its shape is
        // judged the same way, then each entry's.
        ValidateLlmShape(section, LlmSection.SectionPath, messages);
        ValidateInlineKey(document, section, messages);
        ValidateProfiles(section, messages);
    }

    /// <summary>
    /// The keys of a section of the <c>Llm</c> shape, judged as the run reads them at start
    /// (<c>LlmSettings</c>, STUDIO-55): strings, a finite temperature, three integers, two switches, an
    /// absolute http(s) address, a variable's name, and a key that is no <c>${NAME}</c> placeholder.
    /// One error per field, at its path; never the value of <c>ApiKeyEnvVar</c> or <c>ApiKey</c>,
    /// which may be a key pasted in the wrong field.
    /// </summary>
    private static void ValidateLlmShape(JsonObject section, string sectionPath, List<ValidationMessage> messages)
    {
        foreach (var key in LlmStringKeys.Where(key => Child(section, key) is JsonObject or JsonArray))
            messages.Add(WrongType($"{sectionPath}:{key}", "a string"));

        if (Child(section, "Temperature") is { } temperature
            && !(AppSettingsDocument.ReadDouble(temperature) is { } value && double.IsFinite(value)))
        {
            messages.Add(WrongType($"{sectionPath}:Temperature", "a finite number"));
        }

        foreach (var key in LlmIntegerKeys.Where(key => Child(section, key) is { } node && AppSettingsDocument.ReadInt32(node) is null))
            messages.Add(WrongType($"{sectionPath}:{key}", "a whole number"));

        foreach (var key in LlmBooleanKeys.Where(key => Child(section, key) is { } node && AppSettingsDocument.ReadBoolean(node) is null))
            messages.Add(WrongType($"{sectionPath}:{key}", "true or false"));

        if (Child(section, "BaseUrl") is JsonValue baseUrlNode
            && AppSettingsDocument.ReadString(baseUrlNode) is { } baseUrl
            && !string.IsNullOrWhiteSpace(baseUrl)
            && !LlmSection.IsAbsoluteHttpUrl(baseUrl))
        {
            messages.Add(ValidationMessage.Error(
                ValidationCodes.InvalidFieldType,
                string.Create(CultureInfo.InvariantCulture, $"'{baseUrl}' is not an absolute http(s) URL."),
                $"{sectionPath}:BaseUrl"));
        }

        if (Child(section, ConfigurationKeys.LlmApiKeyEnvVar) is JsonValue referenceNode
            && !LlmSection.IsVariableName(AppSettingsDocument.ReadString(referenceNode)))
        {
            messages.Add(ValidationMessage.Error(
                ValidationCodes.InvalidFieldType,
                $"'{sectionPath}:{ConfigurationKeys.LlmApiKeyEnvVar}' must be the name of an environment variable " +
                "(no '=', space or line break) — never the key itself.",
                $"{sectionPath}:{ConfigurationKeys.LlmApiKeyEnvVar}"));
        }

        if (Child(section, "ApiKey") is JsonValue keyNode
            && AppSettingsDocument.ReadString(keyNode) is { } apiKey
            && LlmSection.IsKeyPlaceholder(apiKey))
        {
            messages.Add(ValidationMessage.Error(
                ValidationCodes.LlmApiKeyPlaceholder,
                $"'{sectionPath}:ApiKey' is a ${{…}} placeholder, which the engine never expands — the text itself " +
                "would be sent as the key, and the run refuses to start. Name the variable that holds the key " +
                $"instead, in place of ApiKey: \"{ConfigurationKeys.LlmApiKeyEnvVar}\": \"{LlmSection.PlaceholderVariable(apiKey)}\".",
                $"{sectionPath}:ApiKey"));
        }
    }

    /// <summary>The information on a key written in clear in <c>Llm</c> — a placeholder is an error instead.</summary>
    private static void ValidateInlineKey(AppSettingsDocument document, JsonObject section, List<ValidationMessage> messages)
    {
        if (Child(section, "ApiKey") is JsonObject or JsonArray)
            return;

        if (document.Llm.ApiKey is { Length: > 0 } apiKey
            && !LlmSection.IsKeyPlaceholder(apiKey)
            && !string.Equals(apiKey, LlmPresets.DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal))
        {
            // STUDIO-49: a key the configuration resolves wins, so this one also masks the
            // variable Llm:ApiKeyEnvVar names — and no election removes it: it is not Studio's.
            messages.Add(ValidationMessage.Information(
                ValidationCodes.InlineApiKey,
                "The API key is stored in clear text in this file, and it masks Llm:ApiKeyEnvVar: a key " +
                "the configuration holds wins over the variable the reference names. Prefer removing it — " +
                $"name the variable that holds the key in ApiKeyEnvVar, or set {LlmPresets.DefaultApiKeyEnv}.",
                "Llm:ApiKey"));
        }

        if (string.Equals(document.Llm.ApiKey, LlmPresets.DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal)
            && document.Llm.ApiKeyEnvVar is { Length: > 0 })
        {
            // STUDIO-54: alone, Docker Model Runner's placeholder is no key — its server checks none.
            // Beside a reference — a file edited by hand: an election takes it out —, it masks the
            // variable the reference names, and the run sends it in place of the key.
            messages.Add(ValidationMessage.Information(
                ValidationCodes.InlineApiKey,
                $"The placeholder key '{LlmPresets.DockerModelRunnerApiKeyPlaceholder}' masks Llm:ApiKeyEnvVar: a key " +
                "the configuration holds wins over the variable the reference names, so a run sends the " +
                "placeholder in place of the key. Remove ApiKey — only a Docker Model Runner endpoint takes it, " +
                "and that endpoint needs no variable.",
                "Llm:ApiKey"));
        }
    }

    /// <summary>
    /// <c>Llm:Profiles</c> (STUDIO-55): an object of entries, each an object of the <c>Llm</c> shape,
    /// none named <c>default</c> — the run refuses to start on any of them. Read from the nodes, not by
    /// path: an entry's name is spelt as the file holds it.
    /// </summary>
    private static void ValidateProfiles(JsonObject section, List<ValidationMessage> messages)
    {
        switch (Child(section, ConfigurationKeys.LlmProfiles))
        {
            case null:
                return;
            case not JsonObject:
                messages.Add(WrongType(LlmProfilesSection.SectionPath, "an object of profiles keyed by name"));
                return;
            case JsonObject profiles:
                foreach (var (id, entry) in profiles)
                    ValidateProfile(id, entry, messages);
                return;
        }
    }

    private static void ValidateProfile(string id, JsonNode? entry, List<ValidationMessage> messages)
    {
        var path = $"{LlmProfilesSection.SectionPath}:{id}";
        if (LlmProfilesSection.IsDefault(id))
        {
            messages.Add(ValidationMessage.Error(
                ValidationCodes.LlmProfileReservedName,
                $"The model profile '{id}' carries the reserved name of the default profile, which is the " +
                $"{LlmSection.SectionPath} section itself: the run refuses to start. Rename the profile.",
                path));
        }

        if (entry is JsonObject shape)
            ValidateLlmShape(shape, path, messages);
        else
            messages.Add(WrongType(path, "an object of the Llm section's keys"));
    }

    /// <summary>
    /// The node at <paramref name="relativePath"/> under <paramref name="section"/>, keys compared
    /// without case as the configuration binds them; null when absent.
    /// </summary>
    private static JsonNode? Child(JsonObject section, string relativePath)
    {
        JsonNode? current = section;
        foreach (var segment in relativePath.Split(':'))
        {
            if (current is not JsonObject container)
                return null;
            current = container.FirstOrDefault(property => string.Equals(property.Key, segment, StringComparison.OrdinalIgnoreCase)).Value;
            if (current is null)
                return null;
        }

        return current;
    }

    private static void ValidateTypes(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        // A node that parses as the declared kind is silent; an absent one is silent too --
        // only a value of the wrong shape is reported, one message per field.
        messages.AddRange(StringFields
            .Where(path => document.GetNode(path) is JsonObject or JsonArray)
            .Select(path => WrongType(path, "a string")));

        messages.AddRange(IntegerFields
            .Where(path => document.GetNode(path) is not null && document.GetInt32(path) is null)
            .Select(path => WrongType(path, "a whole number")));

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

    /// <summary>
    /// <c>Orkeon:Rag:LlmProfile</c> (GAP-19) must name a profile the host defines: an unknown one
    /// refuses the start of every run. A warning, not an error — like WIN-01, the profile may come
    /// from the environment the run is launched with (<c>ORKEON_Llm__Profiles__&lt;id&gt;__*</c>,
    /// which is how Studio passes its own model settings), and Studio sees the file alone.
    /// </summary>
    private static void ValidateRagLlmProfile(AppSettingsDocument document, List<ValidationMessage> messages)
    {
        if (document.GetNode(RagSection.LlmProfilePath) is not JsonValue
            || document.Rag.LlmProfile is not { } profile
            || LlmProfilesSection.IsDefault(profile)
            || document.Llm.Profiles.Find(profile) is not null)
        {
            return;
        }

        var known = new[] { LlmProfilesSection.DefaultProfile }.Concat(document.Llm.Profiles.Ids);
        messages.Add(ValidationMessage.Warning(
            ValidationCodes.UnknownRagLlmProfile,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The RAG subsystem names the LLM profile '{profile.Trim()}', which this file does not define: " +
                $"the host refuses to start unless the environment the run is launched with defines it. " +
                $"Profiles in this file: {string.Join(", ", known)}."),
            RagSection.LlmProfilePath));
    }

    private void ValidateMounts(
        AppSettingsDocument document,
        List<ValidationMessage> messages,
        ValidationScope scope)
    {
        var entries = document.Mounts.RawEntries;

        if (entries.Count == 0)
        {
            // A warning, while editing and when saving alike (STUDIO-57). The runner builds no
            // file system when nothing is mounted and runs the crew all the same: a team that
            // reads a mail and answers a mail needs no folder, and a team's own folders travel
            // in its sidecar and its launcher, never in this file. Saving used to block here,
            // on the claim that the runtime refuses to start — it does not.
            messages.Add(ValidationMessage.Warning(
                ValidationCodes.MountsEmpty,
                "No file system mount is declared: the agents see no folder of yours. A team that " +
                "reads or writes no file needs none, and a team's own folders travel with it.",
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
