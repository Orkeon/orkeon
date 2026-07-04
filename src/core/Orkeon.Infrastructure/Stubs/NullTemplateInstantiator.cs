using Microsoft.Extensions.Logging;
using Orkeon.Application.Common;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.Composition;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// Stub implementation of <see cref="ITemplateInstantiator"/> that throws
/// <see cref="NotSupportedException"/> explaining template instantiation is not configured.
/// Logs a warning on first use.
/// </summary>
public sealed partial class NullTemplateInstantiator : ITemplateInstantiator
{
    private readonly ILogger<NullTemplateInstantiator> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="NullTemplateInstantiator"/>.</summary>
    public NullTemplateInstantiator(ILogger<NullTemplateInstantiator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogNullInstantiatorFallback();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using NullTemplateInstantiator — template instantiation is not available. Register a real ITemplateInstantiator for production.")]
    private partial void LogNullInstantiatorFallback();

    /// <inheritdoc />
    public Task<DomainAgent> InstantiateAgentAsync(string templateName, TemplateInstantiationParameters parameters, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        throw new NotSupportedException($"ITemplateInstantiator is not configured. Cannot instantiate agent template '{templateName}'.");
    }

    /// <inheritdoc />
    public Task<DomainAgent> InstantiateAgentAsync(AgentTemplateDefinition templateDefinition, TemplateInstantiationParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateDefinition);
        WarnOnce();
        throw new NotSupportedException($"ITemplateInstantiator is not configured. Cannot instantiate agent template '{templateDefinition.Name}'.");
    }

    /// <inheritdoc />
    public Task<CrewTask> InstantiateTaskAsync(string templateName, TemplateInstantiationParameters parameters, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        throw new NotSupportedException($"ITemplateInstantiator is not configured. Cannot instantiate task template '{templateName}'.");
    }

    /// <inheritdoc />
    public Task<CrewTask> InstantiateTaskAsync(TaskTemplateDefinition templateDefinition, TemplateInstantiationParameters parameters, DomainAgent? assignedAgent = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateDefinition);
        WarnOnce();
        throw new NotSupportedException($"ITemplateInstantiator is not configured. Cannot instantiate task template '{templateDefinition.Name}'.");
    }

    /// <inheritdoc />
    public Task<DomainCrew> ComposeCrewAsync(CrewTemplate crewTemplate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crewTemplate);
        WarnOnce();
        throw new NotSupportedException($"ITemplateInstantiator is not configured. Cannot compose crew from template '{crewTemplate.Name}'.");
    }

    /// <inheritdoc />
    public Task<DomainCrew> ComposeCrewAsync(string name, IEnumerable<(string agentTemplate, TemplateInstantiationParameters parameters)> agents, IEnumerable<(string taskTemplate, TemplateInstantiationParameters parameters)> tasks, ProcessType? processType = null, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        throw new NotSupportedException($"ITemplateInstantiator is not configured. Cannot compose crew '{name}'.");
    }

    /// <inheritdoc />
    public Task<InstantiationValidation> ValidateInstantiationAsync(string templateName, TemplateType templateType, TemplateInstantiationParameters parameters, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(InstantiationValidation.Invalid("ITemplateInstantiator is not configured."));
    }

    /// <inheritdoc />
    public Task<InstantiationPreview> PreviewInstantiationAsync(string templateName, TemplateType templateType, TemplateInstantiationParameters parameters, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(new InstantiationPreview
        {
            TemplateName = templateName,
            RenderedContent = string.Empty,
            Warnings = ["ITemplateInstantiator is not configured."]
        });
    }
}
