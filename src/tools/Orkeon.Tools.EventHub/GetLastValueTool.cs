using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>Agent tool that reads the retained value behind a LastValueCache key.</summary>
[ToolContract("get_last_value",
    Name = "get_last_value",
    Description = "Read the last retained payload for a given key (LastValueCache).",
    Category = "EventHub")]
public sealed class GetLastValueTool : ToolBase<GetLastValueRequest, GetLastValueResponse>, ITool
{
    private readonly IEventHub _hub;
    private readonly IEventHubCallerContext _callerContext;

    /// <summary>Initializes a new instance of <see cref="GetLastValueTool"/>.</summary>
    public GetLastValueTool(
        IEventHub hub,
        IEventHubCallerContext callerContext,
        ILogger<GetLastValueTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(callerContext);
        _hub = hub;
        _callerContext = callerContext;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(GetLastValueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Key))
            return "key is required";
        return null;
    }

    /// <inheritdoc/>
    protected override Task<GetLastValueResponse> ExecuteTypedAsync(
        GetLastValueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CrewId? scope = null;
        if (!string.IsNullOrWhiteSpace(request.CrewScope))
        {
            try { scope = CrewId.Parse(request.CrewScope); }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new ArgumentException($"crew_scope '{request.CrewScope}' is not a valid CrewId.", nameof(request));
            }
        }
        else
        {
            var caller = _callerContext.Current;
            scope = CrewId.IsSystem(caller.CrewId) ? null : caller.CrewId;
        }

        return ExecuteCoreAsync(request, scope, cancellationToken);
    }

    private async Task<GetLastValueResponse> ExecuteCoreAsync(
        GetLastValueRequest request, CrewId? scope, CancellationToken cancellationToken)
    {
        var message = await _hub.GetLastValueAsync(request.Key, scope, cancellationToken).ConfigureAwait(false);
        if (message is null)
            return new GetLastValueResponse { Value = null, SetAt = null, Found = false };

        var envelope = EventHubToolHelpers.ToEnvelope(message);
        return new GetLastValueResponse
        {
            Value = envelope.Payload,
            SetAt = envelope.PublishedAt,
            Found = true
        };
    }
}
