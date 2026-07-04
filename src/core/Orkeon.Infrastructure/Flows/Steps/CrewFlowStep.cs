using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Infrastructure.Flows.Base;

namespace Orkeon.Infrastructure.Flows.Steps;

/// <summary>Typed input for <see cref="CrewFlowStep"/>. Captures runtime context variables.</summary>
public sealed class CrewFlowStepInput
{
    /// <summary>Optional override for the crew configuration key (may also come from parameters).</summary>
    public string? CrewConfig { get; set; }
}

/// <summary>Typed output for <see cref="CrewFlowStep"/>.</summary>
public sealed class CrewFlowStepOutput
{
    /// <summary>Gets or sets the final output text produced by the crew.</summary>
    public string CrewOutput { get; set; } = string.Empty;
    /// <summary>Gets or sets the total crew execution time in seconds.</summary>
    public double CrewDuration { get; set; }
}

/// <summary>
/// Flow step that executes a crew.
/// </summary>
public class CrewFlowStep : FlowStepBase<CrewFlowStepInput, CrewFlowStepOutput>
{
    private readonly ICrewOrchestrationService _crewService;
    private readonly FlowStepParameters _parameters;
    private readonly string _name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Description => $"Executes crew configuration: {_parameters.Get<string>("crew_config") ?? "unknown"}";

    /// <summary>Initializes a new instance of <see cref="CrewFlowStep"/>.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="parameters">The step parameters.</param>
    /// <param name="crewService">The crew orchestration service.</param>
    public CrewFlowStep(
        string name,
        FlowStepParameters parameters,
        ICrewOrchestrationService crewService)
    {
        _name = name;
        _parameters = parameters;
        ArgumentNullException.ThrowIfNull(crewService);
        _crewService = crewService;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(CrewFlowStepInput request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var crewConfig = _parameters.Get<string>("crew_config") ?? request.CrewConfig;
        return string.IsNullOrEmpty(crewConfig) ? "Missing 'crew_config' parameter." : null;
    }

    /// <inheritdoc />
    protected override Task<CrewFlowStepOutput> ExecuteTypedAsync(
        CrewFlowStepInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<CrewFlowStepOutput> ExecuteTypedCoreAsync()
        {
            var crewConfig = _parameters.Get<string>("crew_config") ?? request.CrewConfig
                ?? throw new InvalidOperationException("Missing 'crew_config' parameter.");

            // Build crew input from context — the base class already deserialized from FlowState
            var variables = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(request.CrewConfig))
                variables["crew_config"] = request.CrewConfig;

            var crewInput = new CrewInput(
                InitialContext: crewConfig,
                Variables: variables);

            var crewId = Domain.Common.CrewId.From(
                Guid.TryParse(crewConfig, out var parsedId) ? parsedId : Guid.NewGuid());
            var output = await _crewService.KickoffAsync(crewId, crewInput, cancellationToken).ConfigureAwait(false);

            return new CrewFlowStepOutput
            {
                CrewOutput = output.FinalOutput,
                CrewDuration = output.Duration.TotalSeconds
            };
        }
    }
}
