using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Flows.Steps;

namespace Orkeon.Infrastructure.Flows;

/// <summary>
/// Factory that maps step type strings to IFlowStep instances.
/// </summary>
public sealed class FlowStepExecutor : IFlowStepExecutor
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Initializes a new instance of <see cref="FlowStepExecutor"/>.</summary>
    /// <param name="serviceProvider">The service provider for resolving step dependencies.</param>
    public FlowStepExecutor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public IFlowStep ResolveStep(FlowStep stepDefinition)
    {
        ArgumentNullException.ThrowIfNull(stepDefinition);

#pragma warning disable CA1308 // lowercase is the normalized switch subject for the step-type token
        return stepDefinition.Type.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "crew" => CreateCrewFlowStep(stepDefinition),
            "llm" => CreateLlmFlowStep(stepDefinition),
            "tool" => CreateToolFlowStep(stepDefinition),
            "conditional" => new ConditionalFlowStep(stepDefinition.Name, stepDefinition.Parameters),
            "human_input" => CreateHumanInputFlowStep(stepDefinition),
            "delay" => new DelayFlowStep(stepDefinition.Name, stepDefinition.Parameters),
            _ => throw new InvalidOperationException(
                $"Unknown step type: '{stepDefinition.Type}'. " +
                "Supported types: crew, llm, tool, conditional, human_input, delay")
        };
    }

    private CrewFlowStep CreateCrewFlowStep(FlowStep stepDefinition)
    {
        var crewService = _serviceProvider.GetRequiredService<ICrewOrchestrationService>();
        return new CrewFlowStep(stepDefinition.Name, stepDefinition.Parameters, crewService);
    }

    private LlmFlowStep CreateLlmFlowStep(FlowStep stepDefinition)
    {
        var chatClient = _serviceProvider.GetRequiredService<IChatClient>();
        return new LlmFlowStep(stepDefinition.Name, stepDefinition.Parameters, chatClient);
    }

    private ToolFlowStep CreateToolFlowStep(FlowStep stepDefinition)
    {
        var toolRegistry = _serviceProvider.GetRequiredService<IToolRegistry>();
        return new ToolFlowStep(stepDefinition.Name, stepDefinition.Parameters, toolRegistry);
    }

    private HumanInputFlowStep CreateHumanInputFlowStep(FlowStep stepDefinition)
    {
        var inputProvider = _serviceProvider.GetRequiredService<IHumanInputProvider>();
        return new HumanInputFlowStep(stepDefinition.Name, stepDefinition.Parameters, inputProvider);
    }
}
