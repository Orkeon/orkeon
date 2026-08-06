using System.Globalization;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Cli.Scripting.Progress;

/// <summary>
/// Routes LLM retry activity to the progress surfaces so a turn parked on a reconnection
/// backoff shows <c>✳ Reconnecting to api.moonshot.ai… retry 4/10 in 8s</c> on the status
/// line instead of sitting silent — the visibility that makes a 10-retry budget acceptable
/// on an interactive turn. Also stamps the ambient <see cref="Dispatch.CommandInstance"/>
/// so <c>ps</c>/<c>inspect</c>/the agents pane show the same state.
/// </summary>
/// <remarks>
/// <see cref="OnCallSettled"/> clears only the banner this observer raised (label-guarded),
/// never a crew's own progress published under the same ticket. The single remembered label
/// matches the broker's single-slot display model; with concurrent LLM calls the last writer
/// wins, same as every other publisher.
/// </remarks>
public sealed class LlmRetryProgressObserver : ILlmRetryObserver
{
    private readonly ProgressBroker _broker;
    private volatile string? _lastLabel;

    public LlmRetryProgressObserver(ProgressBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
    }

    /// <inheritdoc />
    public void OnRetryScheduled(LlmRetryEvent retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        var instance = ProgressAmbient.CurrentInstance;
        var target = string.IsNullOrEmpty(retry.Host) ? retry.Provider : retry.Host;
        var label = "Reconnecting to " + target;
        var message = string.Format(
            CultureInfo.InvariantCulture,
            "retry {0}/{1} in {2:0.#}s{3}",
            retry.Attempt, retry.MaxRetries, retry.Delay.TotalSeconds,
            string.IsNullOrEmpty(retry.Reason) ? "" : " — " + retry.Reason);

        _broker.Report(new ProgressSnapshot
        {
            Label = label,
            Message = message,
            Ticket = instance?.Ticket,
        });
        _lastLabel = label;

        instance?.ReportProgress(new Dispatch.CommandProgress(step: null, percent: null, message: label + " — " + message));
    }

    /// <inheritdoc />
    public void OnCallSettled()
    {
        var label = _lastLabel;
        if (label is null) return;
        _lastLabel = null;
        _broker.ClearLabel(label);
    }
}
