using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="SessionStoreTool"/> (exp 07 SPEC §7.3).</summary>
public sealed class SessionStoreRequest
{
    /// <summary>Operation: read_messages / write_messages / append_note / get_metadata / set_title / truncate / reset.</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "read_messages";

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
}

/// <summary>
/// Read/write access to the session conversation buffer and metadata — the pivot tool of the
/// coding agent (exp 07 SPEC §7.3). Backed by <see cref="ISessionBufferService"/>.
/// </summary>
public sealed class SessionStoreTool : ToolBase<SessionStoreRequest, SessionStoreResponse>
{
    /// <inheritdoc />
    public override string Name => "session_store";
    /// <inheritdoc />
    public override string Description =>
        "Read, write, truncate, and annotate the current session conversation buffer and metadata.";

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
            "append_note" => new SessionStoreResponse { Success = _buffer.AppendNote(request.Note ?? "") },
            "get_metadata" => new SessionStoreResponse { Success = true, Metadata = _buffer.GetMetadata() },
            "set_title" => new SessionStoreResponse { Success = _buffer.SetTitle(request.Title ?? "") },
            "truncate" => new SessionStoreResponse { Success = true, RemovedCount = _buffer.Truncate(request.RetainCount) },
            "reset" => new SessionStoreResponse { Success = true, RemovedCount = _buffer.Reset() },
            _ => new SessionStoreResponse { Success = false },
        };
        return Task.FromResult(response);
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
