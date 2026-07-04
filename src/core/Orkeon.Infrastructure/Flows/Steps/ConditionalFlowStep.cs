using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Infrastructure.Flows.Steps;

/// <summary>
/// Flow step that evaluates a condition and directs the flow
/// to a true_step or false_step branch.
/// </summary>
public class ConditionalFlowStep : IFlowStep
{
    private readonly FlowStepParameters _parameters;

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public string Description => "Evaluates a condition and branches the flow.";

    /// <summary>Initializes a new instance of <see cref="ConditionalFlowStep"/>.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="parameters">The step parameters.</param>
    public ConditionalFlowStep(string name, FlowStepParameters parameters)
    {
        Name = name;
        _parameters = parameters;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Flow-step fault barrier: any failure evaluating the conditional step is converted into a failed FlowStepResult so it cannot crash the flow engine.")]
    public Task<FlowStepResult> ExecuteAsync(FlowState context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var conditionKey = _parameters.Get<string>("condition_key");
            if (string.IsNullOrEmpty(conditionKey))
            {
                return Task.FromResult(
                    FlowStepResult.CreateFailure("Missing 'condition_key' parameter."));
            }

            var trueStep = _parameters.Get<string>("true_step");
            var falseStep = _parameters.Get<string>("false_step");

            // Evaluate condition from context
            var conditionValue = context.Get<object>(conditionKey);
            var conditionResult = EvaluateCondition(conditionValue);

            var nextStep = conditionResult ? trueStep : falseStep;

            var updatedContext = FlowState.Empty
                .Set("condition_result", conditionResult);

            return Task.FromResult(new FlowStepResult
            {
                Success = true,
                Output = conditionResult,
                NextStep = nextStep,
                UpdatedContext = updatedContext
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                FlowStepResult.CreateFailure($"Conditional step '{Name}' failed: {ex.Message}"));
        }
    }

    private static bool EvaluateCondition(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            int i => i != 0,
            double d => d != 0.0,
            _ => true
        };
    }
}
