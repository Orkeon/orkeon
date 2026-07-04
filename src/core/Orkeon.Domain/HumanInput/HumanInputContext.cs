using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.HumanInput;

namespace Orkeon.Domain.HumanInput;

/// <summary>
/// Context information for human input requests.
/// </summary>
public class HumanInputContext
{
    /// <summary>Gets the unique request identifier.</summary>
    public HumanInputRequestId RequestId { get; init; } = HumanInputRequestId.Create();
    /// <summary>Gets the agent identifier requesting input.</summary>
    public AgentId AgentId { get; init; } = AgentId.Create();
    /// <summary>Gets the role of the requesting agent.</summary>
    public string AgentRole { get; init; } = string.Empty;
    /// <summary>Gets the task identifier for which input is needed.</summary>
    public TaskId TaskId { get; init; } = TaskId.Create();
    /// <summary>Gets the description of the task.</summary>
    public string TaskDescription { get; init; } = string.Empty;
    /// <summary>Gets the prompt shown to the human.</summary>
    public string Prompt { get; init; } = string.Empty;
    /// <summary>Gets the expected input type (e.g. "text", "choice", "confirmation").</summary>
    public string InputType { get; init; } = HumanInputDefaults.TextInputType;
    /// <summary>Gets the available options for choice inputs.</summary>
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    /// <summary>Gets additional metadata for the request.</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    /// <summary>Gets the optional timeout for the response.</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>Gets whether this input is required.</summary>
    public bool IsRequired { get; init; } = true;
    /// <summary>Gets the optional default value if no input is provided.</summary>
    public string? DefaultValue { get; init; }
    /// <summary>Gets validation rules for the input.</summary>
    public Dictionary<string, object> ValidationRules { get; init; } = [];
    /// <summary>Gets when this context was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Creates a text input context.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="prompt">The prompt for the human.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <returns>A <see cref="HumanInputContext"/> configured for text input.</returns>
    public static HumanInputContext CreateTextInput(
        AgentId agentId,
        TaskId taskId,
        string prompt,
        TimeSpan? timeout = null)
    {
        return new HumanInputContext
        {
            AgentId = agentId,
            TaskId = taskId,
            Prompt = prompt,
            InputType = HumanInputDefaults.TextInputType,
            Timeout = timeout
        };
    }

    /// <summary>Creates a choice input context.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="prompt">The prompt for the human.</param>
    /// <param name="options">The available choices.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <returns>A <see cref="HumanInputContext"/> configured for choice input.</returns>
    public static HumanInputContext CreateChoiceInput(
        AgentId agentId,
        TaskId taskId,
        string prompt,
        IReadOnlyList<string> options,
        TimeSpan? timeout = null)
    {
        return new HumanInputContext
        {
            AgentId = agentId,
            TaskId = taskId,
            Prompt = prompt,
            InputType = HumanInputDefaults.ChoiceInputType,
            Options = options,
            Timeout = timeout
        };
    }

    /// <summary>Creates a confirmation input context.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="prompt">The prompt for the human.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <returns>A <see cref="HumanInputContext"/> configured for yes/no confirmation.</returns>
    public static HumanInputContext CreateConfirmationInput(
        AgentId agentId,
        TaskId taskId,
        string prompt,
        TimeSpan? timeout = null)
    {
        return new HumanInputContext
        {
            AgentId = agentId,
            TaskId = taskId,
            Prompt = prompt,
            InputType = HumanInputDefaults.ConfirmationInputType,
            Options = ["Yes", "No"],
            Timeout = timeout
        };
    }
}
