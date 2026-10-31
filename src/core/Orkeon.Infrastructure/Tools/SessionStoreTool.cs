using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="SessionStoreTool"/>.</summary>
public sealed class SessionStoreRequest
{
    /// <summary>Operation: read_messages / write_messages / append_message / append_note / get_metadata / set_title / truncate / reset / get_state / set_state.</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "read_messages";

    /// <summary>Role of the message append_message adds (user, assistant, system, note).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Content of the message append_message adds.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>New buffer contents for write_messages.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<object>? Messages { get; set; }

    /// <summary>Note text for append_note.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Title for set_title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Number of recent messages to keep for truncate.</summary>
    [JsonPropertyName("retain_count")]
    public int RetainCount { get; set; } = 3;

    /// <summary>State key for get_state / set_state (session-scoped key/value bag).</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>State value for set_state; null removes the key.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Several state keys: read in one call by get_state (the answer is values), removed in one call by set_state.</summary>
    [JsonPropertyName("keys")]
    public IReadOnlyList<string>? Keys { get; set; }

    /// <summary>Several state entries for set_state, written in one call (the tool pipeline drops a null value: a key is removed through keys).</summary>
    [JsonPropertyName("values")]
    public IReadOnlyDictionary<string, string>? Values { get; set; }
}

/// <summary>Typed response for <see cref="SessionStoreTool"/>.</summary>
public sealed class SessionStoreResponse
{
    /// <summary>Operation outcome.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Buffer contents for read_messages / write_messages.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<object>? Messages { get; set; }

    /// <summary>Session metadata for get_metadata.</summary>
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }

    /// <summary>Messages removed by truncate / reset.</summary>
    [JsonPropertyName("removed_count")]
    public int RemovedCount { get; set; }

    /// <summary>State value for get_state (null when the key was never set).</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>The state entries get_state read (keys) or set_state wrote (values); a key never set is absent.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyDictionary<string, string>? Values { get; set; }
}

/// <summary>
/// Read/write access to the session conversation buffer and metadata — the pivot tool of a
/// coding agent session. Backed by <see cref="ISessionBufferService"/>.
/// </summary>
public sealed class SessionStoreTool : ToolBase<SessionStoreRequest, SessionStoreResponse>
{
    /// <inheritdoc />
    public override string Name => "session_store";
    /// <inheritdoc />
    public override string Description =>
        "Read, write, append to, truncate, and annotate the current session conversation buffer and metadata; get_state reads one key or several (keys), set_state writes one (key, value) or several (values) and removes several (keys).";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly ISessionBufferService _buffer;

    /// <summary>Creates the tool with its backing service(s).</summary>
    public SessionStoreTool(ISessionBufferService buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <inheritdoc />
    protected override Task<SessionStoreResponse> ExecuteTypedAsync(
        SessionStoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var op = (request.Operation ?? "read_messages").Trim();
        SessionStoreResponse response = op switch
        {
            "read_messages" => new SessionStoreResponse
            {
                Success = true,
                Messages = _buffer.GetMessages().ToArray<object>(),
            },
            "write_messages" => WriteMessages(request),
            "append_message" => AppendMessage(request),
            "append_note" => new SessionStoreResponse { Success = _buffer.AppendNote(request.Note ?? "") },
            "get_metadata" => new SessionStoreResponse { Success = true, Metadata = _buffer.GetMetadata() },
            "set_title" => new SessionStoreResponse { Success = _buffer.SetTitle(request.Title ?? "") },
            "truncate" => new SessionStoreResponse { Success = true, RemovedCount = _buffer.Truncate(request.RetainCount) },
            "reset" => new SessionStoreResponse { Success = true, RemovedCount = _buffer.Reset() },
            "get_state" => GetState(request),
            "set_state" => SetState(request),
            _ => new SessionStoreResponse { Success = false },
        };
        return Task.FromResult(response);
    }

    /// <summary>Reads one key (value) or several keys (values, a key never set absent); refused without either.</summary>
    private SessionStoreResponse GetState(SessionStoreRequest request)
    {
        if (request.Keys is { Count: > 0 } keys)
        {
            if (keys.Any(string.IsNullOrWhiteSpace))
                return new SessionStoreResponse { Success = false };
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (_buffer.GetState(key) is { } value)
                    values[key] = value;
            }
            return new SessionStoreResponse { Success = true, Values = values };
        }

        if (string.IsNullOrWhiteSpace(request.Key))
            return new SessionStoreResponse { Success = false };
        return new SessionStoreResponse { Success = true, Value = _buffer.GetState(request.Key) };
    }

    /// <summary>Writes one key (key, value), or several (values) and removes several (keys); refused without any.</summary>
    private SessionStoreResponse SetState(SessionStoreRequest request)
    {
        var values = request.Values is { Count: > 0 } ? request.Values : null;
        var removed = request.Keys is { Count: > 0 } ? request.Keys : null;
        if (values is not null || removed is not null)
        {
            if (values?.Keys.Any(string.IsNullOrWhiteSpace) == true || removed?.Any(string.IsNullOrWhiteSpace) == true)
                return new SessionStoreResponse { Success = false };
            if (values is not null)
            {
                foreach (var (key, value) in values)
                    _buffer.SetState(key, value);
            }
            if (removed is not null)
            {
                foreach (var key in removed)
                    _buffer.SetState(key, null);
            }
            return new SessionStoreResponse { Success = true, Values = values is null ? [] : new Dictionary<string, string>(values, StringComparer.Ordinal) };
        }

        if (string.IsNullOrWhiteSpace(request.Key))
            return new SessionStoreResponse { Success = false };
        _buffer.SetState(request.Key, request.Value);
        return new SessionStoreResponse { Success = true, Value = request.Value };
    }

    /// <summary>Appends one message (role, content) at the end of the buffer; the buffer is neither returned nor rewritten.</summary>
    private SessionStoreResponse AppendMessage(SessionStoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Role) || request.Content is null)
            return new SessionStoreResponse { Success = false };
        _buffer.AppendMessage(new SessionMessage { Role = request.Role.Trim(), Content = request.Content });
        return new SessionStoreResponse { Success = true };
    }

    private SessionStoreResponse WriteMessages(SessionStoreRequest request)
    {
        var parsed = (request.Messages ?? Array.Empty<object>())
            .Select(ToSessionMessage)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();
        _buffer.ReplaceMessages(parsed);
        return new SessionStoreResponse { Success = true, Messages = parsed.ToArray<object>() };
    }

    /// <summary>Coerces a raw buffer element (JsonElement or dictionary) into a <see cref="SessionMessage"/>.</summary>
    private static SessionMessage? ToSessionMessage(object? raw)
    {
        switch (raw)
        {
            case SessionMessage sm:
                return sm;
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                return new SessionMessage
                {
                    Role = GetJsonString(je, "role") ?? "user",
                    Content = GetJsonString(je, "content") ?? "",
                    Timestamp = GetJsonString(je, "timestamp"),
                };
            case IDictionary<string, object?> d:
                return new SessionMessage
                {
                    Role = GetDictString(d, "role") ?? "user",
                    Content = GetDictString(d, "content") ?? "",
                    Timestamp = GetDictString(d, "timestamp"),
                };
            default:
                return null;
        }
    }

    private static string? GetJsonString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetDictString(IDictionary<string, object?> d, string name)
        => d.TryGetValue(name, out var v) ? v?.ToString() : null;
}
