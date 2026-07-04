using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Infrastructure.Flows.Base;

/// <summary>
/// Generic typed base class for flow steps. Bridges IFlowStep with the
/// ComponentBase Dict-Model pipeline for strongly typed step execution.
/// </summary>
public abstract class FlowStepBase<TInput, TOutput> : ComponentBase<TInput, TOutput>, IFlowStep
    where TInput : class, new()
    where TOutput : class
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>
    /// Optional step dependencies. Default: none.
    /// </summary>
    public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Flow-step boundary fault barrier: any failure in a concrete step's typed pipeline is converted into a failed FlowStepResult so one step cannot crash the flow engine.")]
    public virtual Task<FlowStepResult> ExecuteAsync(FlowState context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<FlowStepResult> ExecuteCoreAsync()
        {
            try
            {
                // Step 1: Convert FlowState to dictionary with nullable value type
                var parameters = context.ToDictionary()
                    .ToDictionary<KeyValuePair<string, object>, string, object?>(
                        kvp => kvp.Key,
                        kvp => kvp.Value);

                // Step 2: Deserialize to typed input
                var typedInput = DeserializeRequest(parameters);

                // Step 3: Validate
                var validationError = ValidateTypedRequest(typedInput);
                if (validationError is not null)
                {
                    return FlowStepResult.CreateFailure($"Validation failed: {validationError}");
                }

                // Step 4: Execute typed step logic
                var typedOutput = await ExecuteTypedAsync(typedInput, cancellationToken).ConfigureAwait(false);

                // Step 5: Serialize output to dictionary
                var outputDict = SerializeResponse(typedOutput);

                // Step 6: Create success result with updated context
                var nonNullDict = outputDict
                    .Where(kvp => kvp.Value is not null)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);
                var updatedContext = FlowState.FromDictionary(nonNullDict);
                return new FlowStepResult
                {
                    Success = true,
                    Output = typedOutput,
                    UpdatedContext = updatedContext
                };
            }
            catch (JsonException ex)
            {
                return FlowStepResult.CreateFailure($"Invalid step parameters: {ex.Message}");
            }
            catch (Exception ex)
            {
                return FlowStepResult.CreateFailure($"Step execution error: {ex.Message}");
            }
        }
    }
}
