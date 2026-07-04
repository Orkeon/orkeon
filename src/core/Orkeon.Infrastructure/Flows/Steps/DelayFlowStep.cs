using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Infrastructure.Flows.Base;

namespace Orkeon.Infrastructure.Flows.Steps;

/// <summary>Typed input for <see cref="DelayFlowStep"/>. Empty — all config comes from parameters.</summary>
public sealed class DelayFlowStepInput
{
    /// <summary>Optional runtime override for delay in milliseconds.</summary>
    public int? DelayMs { get; set; }

    /// <summary>Optional runtime override for delay in seconds.</summary>
    public int? DelaySeconds { get; set; }
}

/// <summary>Typed output for <see cref="DelayFlowStep"/>.</summary>
public sealed class DelayFlowStepOutput
{
    /// <summary>Gets or sets a message describing the delay that was applied.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Flow step that introduces a configurable delay.
/// </summary>
public class DelayFlowStep : FlowStepBase<DelayFlowStepInput, DelayFlowStepOutput>
{
    private readonly FlowStepParameters _parameters;
    private readonly string _name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Description => "Waits for a configurable duration.";

    /// <summary>Initializes a new instance of <see cref="DelayFlowStep"/>.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="parameters">The step parameters.</param>
    public DelayFlowStep(string name, FlowStepParameters parameters)
    {
        _name = name;
        _parameters = parameters;
    }

    /// <inheritdoc />
    protected override Task<DelayFlowStepOutput> ExecuteTypedAsync(
        DelayFlowStepInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<DelayFlowStepOutput> ExecuteTypedCoreAsync()
        {
            var delayMs = request.DelayMs ?? _parameters.Get<int>("delay_ms");
            if (delayMs <= 0)
            {
                var delaySec = request.DelaySeconds ?? _parameters.Get<int>("delay_seconds");
                if (delaySec > 0)
                    delayMs = delaySec * 1000;
                else
                    delayMs = 1000; // Default 1 second
            }

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

            return new DelayFlowStepOutput
            {
                Message = $"Delayed for {delayMs}ms"
            };
        }
    }
}
