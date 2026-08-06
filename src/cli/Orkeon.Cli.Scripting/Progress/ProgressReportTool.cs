using System.Text.Json.Serialization;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Cli.Scripting.Progress;

/// <summary>Typed request for <see cref="ProgressReportTool"/>.</summary>
public sealed class ProgressReportRequest
{
    /// <summary>Headline of the running operation, e.g. "Compacting conversation".</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>Current step (1-based) for step-wise operations.</summary>
    [JsonPropertyName("step")]
    public int? Step { get; set; }

    /// <summary>Total step count, when known.</summary>
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    /// <summary>Completion ratio in [0, 100], for operations that can measure one.</summary>
    [JsonPropertyName("percent")]
    public double? Percent { get; set; }

    /// <summary>Free-form current-phase line, e.g. "extracting memories".</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>True finalises the operation: the broker slot is released.</summary>
    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

/// <summary>Typed response for <see cref="ProgressReportTool"/>.</summary>
public sealed class ProgressReportResponse
{
    /// <summary>Always true — reporting progress is fire-and-forget by design.</summary>
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; }
}

/// <summary>
/// Host-side progress channel for CREW scripts (<c>Tools.progressReport(...)</c> from a
/// <c>.ork.ts</c> body): publishes to the <see cref="ProgressBroker"/> (TUI status line)
/// and, when the call happens inside detached command work, to the owning
/// <see cref="CommandInstance"/> (surfaced by <c>ps</c>/<c>inspect</c> and the agents pane).
/// </summary>
/// <remarks>
/// Command scripts have <c>ctx.progress(...)</c> for the same purpose; crews have no
/// <c>ctx.services</c> and reach the host exclusively through the <c>tools</c> global,
/// which is why this is a tool and not a context method. Deliberately absent from the
/// exp07 LLM catalogue: it is plumbing for scripted orchestration, not an agent capability.
/// </remarks>
public sealed class ProgressReportTool : ToolBase<ProgressReportRequest, ProgressReportResponse>
{
    /// <inheritdoc />
    public override string Name => "progress_report";

    /// <inheritdoc />
    public override string Description =>
        "Report the progress of a long-running scripted operation (label, step/total or percent, message).";

    /// <summary>Only mutates host-side display state — never the workspace.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly ProgressBroker _broker;

    /// <summary>Creates the tool over the shared broker.</summary>
    public ProgressReportTool(ProgressBroker broker)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    /// <inheritdoc />
    protected override Task<ProgressReportResponse> ExecuteTypedAsync(
        ProgressReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var label = string.IsNullOrWhiteSpace(request.Label) ? "Working" : request.Label.Trim();
        var instance = ProgressAmbient.CurrentInstance;

        if (request.Done)
        {
            _broker.ClearLabel(label);
        }
        else
        {
            _broker.Report(new ProgressSnapshot
            {
                Label = label,
                Step = request.Step,
                Total = request.Total,
                Percent = request.Percent,
                Message = request.Message,
                StartedAt = DateTimeOffset.UtcNow,
                Ticket = instance?.Ticket,
            });
            instance?.ReportProgress(new CommandProgress(request.Step, request.Percent, request.Message ?? label));
        }

        return Task.FromResult(new ProgressReportResponse { Acknowledged = true });
    }
}
