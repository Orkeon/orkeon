using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="SessionSnipTool"/>.</summary>
public sealed class SessionSnipRequest
{
    /// <summary>Number of recent exchanges to keep.</summary>
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
}

/// <summary>
/// Atomically truncates the session buffer to a minimal window (head + last N) — a fast,
/// destructive alternative to <c>/compact</c> (exp 07 SPEC §7.3; backs <c>/force-snip</c>).
/// </summary>
public sealed class SessionSnipTool : ToolBase<SessionSnipRequest, SessionSnipResponse>
{
    /// <inheritdoc />
    public override string Name => "session_snip";
    /// <inheritdoc />
    public override string Description =>
        "Immediately truncate the conversation to a minimal window without analysis.";

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
        var removed = _buffer.Truncate(request.RetainCount);
        return Task.FromResult(new SessionSnipResponse
        {
            RemovedCount = removed,
            RetainedCount = _buffer.MessageCount,
        });
    }
}
