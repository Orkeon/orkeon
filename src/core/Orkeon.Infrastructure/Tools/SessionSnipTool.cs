using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="SessionSnipTool"/>.</summary>
public sealed class SessionSnipRequest
{
    /// <summary>Number of recent turns to keep; a turn is a user request with the tool traces and the answer that follow it.</summary>
    [JsonPropertyName("retain_count")]
    public int RetainCount { get; set; } = 3;
}

/// <summary>Typed response for <see cref="SessionSnipTool"/>.</summary>
public sealed class SessionSnipResponse
{
    /// <summary>Messages removed.</summary>
    [JsonPropertyName("removed_count")]
    public int RemovedCount { get; set; }

    /// <summary>Messages retained.</summary>
    [JsonPropertyName("retained_count")]
    public int RetainedCount { get; set; }

    /// <summary>Index, in the buffer before the cut, of the first message of the kept tail; -1 when nothing was removed or when the head alone was kept.</summary>
    [JsonPropertyName("kept_from")]
    public int KeptFrom { get; set; }

    /// <summary>Role of the message at <see cref="KeptFrom"/>; null when <see cref="KeptFrom"/> is -1.</summary>
    [JsonPropertyName("kept_from_role")]
    public string? KeptFromRole { get; set; }
}

/// <summary>
/// Atomically truncates the session buffer to a minimal window (head + last N turns) — a fast,
/// destructive alternative to <c>/compact</c> (backs <c>/force-snip</c>).
/// </summary>
public sealed class SessionSnipTool : ToolBase<SessionSnipRequest, SessionSnipResponse>
{
    /// <inheritdoc />
    public override string Name => "session_snip";
    /// <inheritdoc />
    public override string Description =>
        "Immediately truncate the conversation to the head message plus the last retain_count turns, without analysis; "
        + "a turn is a user request with the tool traces and the answer that follow it, so the cut never splits one.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly ISessionBufferService _buffer;

    /// <summary>Creates the tool with its backing service(s).</summary>
    public SessionSnipTool(ISessionBufferService buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <inheritdoc />
    protected override Task<SessionSnipResponse> ExecuteTypedAsync(
        SessionSnipRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cut = _buffer.Truncate(request.RetainCount);
        return Task.FromResult(new SessionSnipResponse
        {
            RemovedCount = cut.RemovedCount,
            RetainedCount = _buffer.MessageCount,
            KeptFrom = cut.KeptFromIndex,
            KeptFromRole = cut.KeptFromRole,
        });
    }
}
