using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// The key a call is filed under in a cassette: the SHA-256 of the profile, the messages, the system
/// message, the tools and the tool mode, the response format, the grammar, the model, the sampling
/// options (temperature, top-p, seed, penalties, stop sequences, maximum tokens, thinking) and the
/// <c>system_message</c> custom parameter - everything of the per-call configuration that shapes the answer - with the timestamps and the generated identifiers the run injected into the
/// text replaced by a fixed mark and the secret-shaped values redacted as the cassette stores them, so that a second run asks for the same key.
/// </summary>
internal static partial class LlmCallKey
{
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})?", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Identifier();

    /// <summary>The key of a chat call.</summary>
    public static string Of(string profile, LlmMessage[] messages, LlmConfig? config)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(messages);
        var request = new
        {
            profile,
            messages = messages.Select(m => new
            {
                role = m.Role,
                content = Normalize(m.Content),
                name = m.Name,
                toolCallId = m.ToolCallId,
                rawToolCalls = m.RawToolCalls is null ? null : Normalize(m.RawToolCalls),
                multiModal = m.MultiModalContent is null ? null : Serialize(m.MultiModalContent) + ImagesKey(m.MultiModalContent),
            }),
            systemMessage = config?.SystemMessage is null ? null : Normalize(config.SystemMessage),
            tools = config?.Tools is null ? null : SerializeTools(config.Tools),
            // Defaults weigh as nothing: a call without a configuration and one with the default
            // configuration ask the same.
            toolMode = ToolModeKey(config),
            responseFormat = config?.ResponseFormat is null ? null : Serialize(config.ResponseFormat),
            grammar = config is { GrammarEnabled: true } ? config.GrammarGbnf ?? string.Empty : null,
            model = string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model,
            temperature = config?.Temperature,
            topP = config?.TopP,
            seed = config?.Seed,
            frequencyPenalty = config is { FrequencyPenalty: not 0 } ? config.FrequencyPenalty : (double?)null,
            presencePenalty = config is { PresencePenalty: not 0 } ? config.PresencePenalty : (double?)null,
            stop = config is { StopSequences.Count: > 0 } ? config.StopSequences : null,
            maxTokens = config?.MaxTokens,
            thinking = config?.Thinking is null ? null : Serialize(config.Thinking),
            // The one custom parameter the providers send as part of the request (the system message
            // of a configuration written before SystemMessage existed); the others are the profile's
            // plumbing (the mock's responses file, an API version) and a replay on another profile
            // must still find the call.
            customSystemMessage = CustomSystemMessageKey(config),
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    }

    /// <summary>The key of a prompt call: the key of a chat call holding that one user message.</summary>
    public static string OfPrompt(string profile, string prompt, LlmConfig? config) =>
        Of(profile, [new LlmMessage { Role = "user", Content = prompt ?? string.Empty }], config);

    /// <summary>A short, normalized excerpt of the last user message, for a miss to name the call.</summary>
    public static string Excerpt(LlmMessage[] messages)
    {
        var last = Array.FindLast(messages, m => m.Role == "user") ?? (messages.Length > 0 ? messages[^1] : null);
        var text = Normalize(last?.Content ?? string.Empty);
        return text.Length <= 80 ? text : text[..80] + "...";
    }

    // The parts are serialized as their base type, which says neither the bytes nor the address of an image.
    private static string ImagesKey(Orkeon.Domain.SharedKernel.ValueObjects.Content.MultiModalContent content) =>
        string.Concat(content.Parts.OfType<Orkeon.Domain.SharedKernel.ValueObjects.Content.ImageContentPart>().Select(image =>
            $"|{image.MimeType}:{image.Uri}:{(image.Data is { } data ? Convert.ToHexStringLower(SHA256.HashData([.. data])) : string.Empty)}"));

    private static string? ToolModeKey(LlmConfig? config) =>
        config is null || config.ToolMode == Orkeon.Domain.Tools.Protocol.ToolCallMode.Auto ? null : config.ToolMode.ToString();

    private static string? CustomSystemMessageKey(LlmConfig? config) =>
        config?.CustomParameters.TryGetValue("system_message", out var custom) == true && custom is not null
            ? Normalize(custom.ToString() ?? string.Empty)
            : null;

    private static string Normalize(string text) =>
        Identifier().Replace(Timestamp().Replace(CassetteRedaction.Text(text), "<time>"), "<id>");

    /// <summary>
    /// The members a tool schema gained after v1.2.0, with the value each holds when it is not set. A member that
    /// holds it is left out of the key, so that a cassette recorded by v1.2.0 still answers a call whose tools set
    /// none of them; a member that is set changes the key.
    /// </summary>
    private static readonly (string Name, bool? Default)[] AddedParameterMembers =
    [
        (nameof(ParameterSchema.Properties), null),
        (nameof(ParameterSchema.Items), null),
        (nameof(ParameterSchema.AnyOf), null),
        (nameof(ParameterSchema.AllowsAdditionalProperties), true),
        (nameof(ParameterSchema.Minimum), null),
        (nameof(ParameterSchema.Maximum), null),
        (nameof(ParameterSchema.ExclusiveMinimum), null),
        (nameof(ParameterSchema.ExclusiveMaximum), null),
        (nameof(ParameterSchema.MinLength), null),
        (nameof(ParameterSchema.MaxLength), null),
        (nameof(ParameterSchema.Pattern), null),
        (nameof(ParameterSchema.MinItems), null),
        (nameof(ParameterSchema.MaxItems), null),
    ];

    private static string SerializeTools(IReadOnlyList<ToolSchema> tools)
    {
        try
        {
            return Normalize(ToolsKeyJson(tools));
        }
        catch (NotSupportedException)
        {
            return Normalize(tools.ToString() ?? string.Empty);
        }
        catch (InvalidOperationException)
        {
            return Normalize(tools.ToString() ?? string.Empty);
        }
    }

    /// <summary>
    /// The JSON the key reads for a tool list: its serialization, without the members added after v1.2.0 that hold
    /// their default - the very text v1.2.0 serialized for the same tools. Only the schema positions are trimmed
    /// (a tool, its parameters, and their values, items, item properties, properties and alternatives); a default,
    /// an example, a return type or a type definition is kept whole.
    /// </summary>
    internal static string ToolsKeyJson(IReadOnlyList<ToolSchema> tools)
    {
        var node = JsonSerializer.SerializeToNode(tools, tools.GetType());
        if (node is JsonArray array)
        {
            foreach (var tool in array.OfType<JsonObject>())
            {
                RemoveDefault(tool, nameof(ToolSchema.AllowsAdditionalParameters), true);
                TrimSchemas(tool[nameof(ToolSchema.Parameters)] as JsonObject);
            }
        }

        return node?.ToJsonString() ?? "null";
    }

    private static void TrimSchemas(JsonObject? schemas)
    {
        if (schemas is null)
            return;
        foreach (var (_, schema) in schemas)
            TrimSchema(schema as JsonObject);
    }

    private static void TrimSchema(JsonObject? schema)
    {
        if (schema is null)
            return;
        foreach (var (name, defaultValue) in AddedParameterMembers)
            RemoveDefault(schema, name, defaultValue);

        TrimSchema(schema[nameof(ParameterSchema.Values)] as JsonObject);
        TrimSchema(schema[nameof(ParameterSchema.Items)] as JsonObject);
        TrimSchemas(schema[nameof(ParameterSchema.ItemProperties)] as JsonObject);
        TrimSchemas(schema[nameof(ParameterSchema.Properties)] as JsonObject);
        if (schema[nameof(ParameterSchema.AnyOf)] is JsonArray alternatives)
        {
            foreach (var alternative in alternatives)
                TrimSchema(alternative as JsonObject);
        }
    }

    /// <summary>Removes <paramref name="name"/> when it holds <paramref name="defaultValue"/> (null, or the boolean given).</summary>
    private static void RemoveDefault(JsonObject schema, string name, bool? defaultValue)
    {
        if (!schema.TryGetPropertyValue(name, out var value))
            return;
        var isDefault = defaultValue is { } flag
            ? value is JsonValue boolean && boolean.TryGetValue<bool>(out var actual) && actual == flag
            : value is null;
        if (isDefault)
            schema.Remove(name);
    }

    private static string Serialize(object value)
    {
        try
        {
            return Normalize(JsonSerializer.Serialize(value));
        }
        catch (NotSupportedException)
        {
            return Normalize(value.ToString() ?? string.Empty);
        }
        catch (InvalidOperationException)
        {
            return Normalize(value.ToString() ?? string.Empty);
        }
    }
}
