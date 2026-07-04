using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using ProcessType = Orkeon.Domain.SharedKernel.ValueObjects.ProcessType;
using Orkeon.Domain.Agent.Composition;
using Orkeon.Application.Common;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Service for instantiating agents and tasks from templates.
/// </summary>
public interface ITemplateInstantiator
{
    /// <summary>
    /// Instantiates an agent from a template.
    /// </summary>
    System.Threading.Tasks.Task<DomainAgent> InstantiateAgentAsync(
        string templateName,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Instantiates an agent from a template definition.
    /// </summary>
    System.Threading.Tasks.Task<DomainAgent> InstantiateAgentAsync(
        AgentTemplateDefinition templateDefinition,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Instantiates a task from a template.
    /// </summary>
    System.Threading.Tasks.Task<CrewTask> InstantiateTaskAsync(
        string templateName,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Instantiates a task from a template definition.
    /// </summary>
    System.Threading.Tasks.Task<CrewTask> InstantiateTaskAsync(
        TaskTemplateDefinition templateDefinition,
        TemplateInstantiationParameters parameters,
        DomainAgent? assignedAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes a complete crew from templates.
    /// </summary>
    System.Threading.Tasks.Task<DomainCrew> ComposeCrewAsync(
        CrewTemplate crewTemplate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes a crew from individual template references.
    /// </summary>
    System.Threading.Tasks.Task<DomainCrew> ComposeCrewAsync(
        string name,
        IEnumerable<(string agentTemplate, TemplateInstantiationParameters parameters)> agents,
        IEnumerable<(string taskTemplate, TemplateInstantiationParameters parameters)> tasks,
        ProcessType? processType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates template instantiation parameters.
    /// </summary>
    System.Threading.Tasks.Task<InstantiationValidation> ValidateInstantiationAsync(
        string templateName,
        TemplateType templateType,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews the instantiation result without creating objects.
    /// </summary>
    System.Threading.Tasks.Task<InstantiationPreview> PreviewInstantiationAsync(
        string templateName,
        TemplateType templateType,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Instantiation validation result.
/// </summary>
public class InstantiationValidation
{
    /// <summary>
    /// Gets whether the instantiation is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the missing parameters.
    /// </summary>
    public IReadOnlyList<string> MissingParameters { get; init; } = [];

    /// <summary>
    /// Gets the invalid parameters.
    /// </summary>
    public InvalidTemplateParameters InvalidParameters { get; set; } = InvalidTemplateParameters.Empty;

    /// <summary>
    /// Gets warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static InstantiationValidation Valid()
    {
        return new InstantiationValidation { IsValid = true };
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    public static InstantiationValidation Invalid(params string[] errors)
    {
        return new InstantiationValidation
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
}

/// <summary>
/// Template type enumeration.
/// </summary>
public enum TemplateType
{
    /// <summary>Agent.</summary>
    Agent,
    /// <summary>Task.</summary>
    Task,
    /// <summary>Both.</summary>
    Both
}

/// <summary>
/// Preview of instantiation result.
/// </summary>
public class InstantiationPreview
{
    /// <summary>
    /// Gets the template name.
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the rendered content.
    /// </summary>
    public string RenderedContent { get; set; } = string.Empty;

    /// <summary>
    /// Gets the resolved parameters.
    /// </summary>
    public ResolvedTemplateParameters ResolvedParameters { get; set; } = ResolvedTemplateParameters.Empty;

    /// <summary>
    /// Gets the effective configuration.
    /// </summary>
    public TemplateConfiguration Configuration { get; set; } = TemplateConfiguration.Empty;

    /// <summary>
    /// Gets any warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
