using Microsoft.Extensions.Logging;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Validation;

/// <summary>
/// Validates crew configuration before execution.
/// Ensures all required components are properly configured.
/// </summary>
public partial class CrewValidator
{
    private readonly ILogger<CrewValidator> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CrewValidator"/>.
    /// </summary>
    public CrewValidator(ILogger<CrewValidator>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CrewValidator>.Instance;
    }

    /// <summary>
    /// Validates a crew configuration for execution readiness.
    /// </summary>
    public ValidationResult ValidateCrew(DomainCrew crew)
    {
        if (crew == null)
            return new ValidationResult(false, ["Crew cannot be null"]);

        var errors = new List<string>();

        ValidateCrewId(crew, errors);
        ValidateCrewAgents(crew, errors);
        ValidateCrewTasks(crew, errors);
        LogValidationResult(errors, crew.Id);

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateCrewId(DomainCrew crew, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(crew.Id))
            errors.Add("Crew must have a valid ID");
    }

    private static void ValidateCrewAgents(DomainCrew crew, List<string> errors)
    {
        if (crew.Agents == null || crew.Agents.Count == 0)
        {
            errors.Add("Crew must have at least one agent");
            return;
        }

        foreach (var agentId in crew.Agents)
        {
            if (agentId == null)
                errors.Add("Crew contains null agent reference");
        }
    }

    private static void ValidateCrewTasks(DomainCrew crew, List<string> errors)
    {
        if (crew.Tasks == null || crew.Tasks.Count == 0)
        {
            errors.Add("Crew must have at least one task");
            return;
        }

        foreach (var taskId in crew.Tasks)
        {
            if (taskId == null)
                errors.Add("Crew contains null task reference");
        }
    }

    private void LogValidationResult(List<string> errors, string crewId)
    {
        if (errors.Count > 0)
            LogValidationFailed(errors.Count, crewId);
        else
            LogValidationPassed(crewId);
    }

    /// <summary>
    /// Validate Agent.
    /// </summary>
    public static IReadOnlyList<string> ValidateAgent(DomainAgent agent)
    {
        var errors = new List<string>();

        if (agent == null)
        {
            errors.Add("DomainAgent cannot be null");
            return errors;
        }

        // Validate required properties
        if (string.IsNullOrWhiteSpace(agent.Role))
        {
            errors.Add($"DomainAgent must have a role");
        }

        if (string.IsNullOrWhiteSpace(agent.Goal))
        {
            errors.Add($"DomainAgent {agent.Role} must have a goal");
        }

        // LlmConfig is optional on the domain entity: a missing config means the
        // runtime default provider is used, so only a present config is validated.
        if (agent.LlmConfig != null)
        {
            errors.AddRange(ValidateLlmConfig(agent.LlmConfig, agent.Role));
        }

        // Validate tools if present
        if (agent.Tools?.Count > 0)
        {
            foreach (var tool in agent.Tools)
            {
                if (tool == null)
                {
                    errors.Add($"DomainAgent {agent.Role} has null tool reference");
                }
                // ToolId is a value object, not a full tool
                // Tool name validation would need to be done by fetching the tool from repository
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates an LLM configuration: non-empty model and sane numeric parameter bounds
    /// (mirroring the canonical bounds enforced by <see cref="LlmConfig.CreateValidated"/>).
    /// </summary>
    /// <remarks>
    /// Provider identity is intentionally not validated here: <see cref="LlmConfig"/> carries
    /// no provider field — the Infrastructure LLM factory infers the provider from
    /// BaseUrl/model heuristics and falls back to OpenAI — and the canonical provider list
    /// lives in the Infrastructure layer, which the Application layer cannot reference.
    /// </remarks>
    /// <param name="llmConfig">The LLM configuration to validate.</param>
    /// <param name="agentRole">The role of the owning agent, used in error messages.</param>
    /// <returns>The list of validation errors; empty when the configuration is valid.</returns>
    public static IReadOnlyList<string> ValidateLlmConfig(LlmConfig llmConfig, string agentRole)
    {
        var errors = new List<string>();

        if (llmConfig == null)
        {
            errors.Add($"DomainAgent {agentRole} has a null LLM config");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(llmConfig.Model))
            errors.Add($"DomainAgent {agentRole} has an LLM config without a model");

        if (llmConfig.Temperature is < 0.0 or > 2.0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM temperature {llmConfig.Temperature} (expected 0.0 to 2.0)"));

        if (llmConfig.TopP is < 0.0 or > 1.0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM top_p {llmConfig.TopP} (expected 0.0 to 1.0)"));

        if (llmConfig.MaxTokens <= 0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM max_tokens {llmConfig.MaxTokens} (expected a positive value)"));

        if (llmConfig.FrequencyPenalty is < -2.0 or > 2.0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM frequency_penalty {llmConfig.FrequencyPenalty} (expected -2.0 to 2.0)"));

        if (llmConfig.PresencePenalty is < -2.0 or > 2.0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM presence_penalty {llmConfig.PresencePenalty} (expected -2.0 to 2.0)"));

        if (llmConfig.TimeoutSeconds <= 0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM timeout {llmConfig.TimeoutSeconds}s (expected a positive value)"));

        if (llmConfig.MaxRetries < 0)
            errors.Add(Inv.Format($"DomainAgent {agentRole} has an invalid LLM max_retries {llmConfig.MaxRetries} (expected a non-negative value)"));

        return errors;
    }

    /// <summary>
    /// Validate Task.
    /// </summary>
    public static IReadOnlyList<string> ValidateTask(CrewTask task)
    {
        var errors = new List<string>();

        if (task == null)
        {
            errors.Add("Task cannot be null");
            return errors;
        }

        // Validate required properties
        if (string.IsNullOrWhiteSpace(task.Description))
        {
            errors.Add("Task must have a description");
        }

        if (string.IsNullOrWhiteSpace(task.ExpectedOutput))
        {
            var descStr = task.Description?.ToString() ?? "";
            var truncated = descStr.Length > 50 ? descStr.Substring(0, 50) : descStr;
            errors.Add($"Task '{truncated}...' must have expected output");
        }

        // Validate agent assignment
        if (task.AssignedAgent != null)
        {
            // Note: We only have AgentId, not the full agent object
            // Validation that the agent exists in crew would need to be done at a higher level
        }

        // Validate context - it's a dictionary, not a list
        // Context is IReadOnlyDictionary<string, string>, no need for null checks on values

        return errors;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew validation failed with {ErrorCount} errors for crew {CrewId}")]
    private partial void LogValidationFailed(int errorCount, string crewId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Crew {CrewId} passed validation")]
    private partial void LogValidationPassed(string crewId);
}

/// <summary>
/// Result of crew validation.
/// </summary>
public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Gets a formatted error message combining all errors.
    /// </summary>
    public string GetErrorMessage() => string.Join("; ", Errors);
}
