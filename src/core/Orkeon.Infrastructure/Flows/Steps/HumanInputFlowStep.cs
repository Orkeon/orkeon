using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Infrastructure.Flows.Base;

namespace Orkeon.Infrastructure.Flows.Steps;

/// <summary>Typed input for <see cref="HumanInputFlowStep"/>.</summary>
public sealed class HumanInputFlowStepInput
{
    /// <summary>Optional prompt override from runtime context.</summary>
    public string? Prompt { get; set; }
}

/// <summary>Typed output for <see cref="HumanInputFlowStep"/>.</summary>
public sealed class HumanInputFlowStepOutput
{
    /// <summary>Gets or sets the input text provided by the human.</summary>
    public string HumanInput { get; set; } = string.Empty;
}

/// <summary>
/// Flow step that awaits human input.
/// </summary>
public class HumanInputFlowStep : FlowStepBase<HumanInputFlowStepInput, HumanInputFlowStepOutput>
{
    private readonly IHumanInputProvider _inputProvider;
    private readonly FlowStepParameters _parameters;
    private readonly string _name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Description => "Requests input from a human user.";

    /// <summary>Initializes a new instance of <see cref="HumanInputFlowStep"/>.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="parameters">The step parameters.</param>
    /// <param name="inputProvider">The human input provider.</param>
    public HumanInputFlowStep(
        string name,
        FlowStepParameters parameters,
        IHumanInputProvider inputProvider)
    {
        _name = name;
        _parameters = parameters;
        ArgumentNullException.ThrowIfNull(inputProvider);
        _inputProvider = inputProvider;
    }

    /// <inheritdoc />
    protected override Task<HumanInputFlowStepOutput> ExecuteTypedAsync(
        HumanInputFlowStepInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<HumanInputFlowStepOutput> ExecuteTypedCoreAsync()
        {
            var prompt = _parameters.Get<string>("prompt")
                         ?? request.Prompt
                         ?? $"Please provide input for step '{_name}':";

            var humanContext = HumanInputContext.CreateTextInput(
                agentId: AgentId.Create(),
                taskId: TaskId.Create(),
                prompt: prompt);

            var input = await _inputProvider.GetInputAsync(humanContext, cancellationToken).ConfigureAwait(false);

            return new HumanInputFlowStepOutput
            {
                HumanInput = input
            };
        }
    }
}
