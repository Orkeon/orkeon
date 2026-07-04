using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Tools.Abstractions.Base;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Infrastructure.Tools;

// ── Request / Response records ────────────────────────────────────────
/// <summary>Request parameters for the ask question tool.</summary>
public record AskQuestionRequest
{
    /// <summary>Gets the question to ask the coworker.</summary>
    [FieldSchema(Description = "The question to ask", Example = "What is the current project deadline?")]
    public string Question { get; init; } = "";

    /// <summary>Gets the additional context for the question.</summary>
    [FieldSchema(Description = "Additional context for the question", Example = "We need to finalize the Q3 roadmap")]
    public string Context { get; init; } = "";

    /// <summary>Gets the role of the coworker to ask.</summary>
    [FieldSchema(Description = "The role of the coworker to ask", Example = "project_manager")]
    public string CoworkerRole { get; init; } = "";
}

/// <summary>Response from the ask question tool.</summary>
public record AskQuestionResponse
{
    /// <summary>Gets the confirmation message with question details.</summary>
    [ReturnSchema(Description = "Confirmation message with delegation details", Example = "Question sent to project_manager. Awaiting response.")]
    public string Message { get; init; } = "";

    /// <summary>Gets the ID of the agent the question was sent to.</summary>
    [ReturnSchema(Description = "ID of the agent the question was sent to", Example = "agent-pm-001")]
    public string TargetAgentId { get; init; } = "";

    /// <summary>Gets the question that was asked.</summary>
    [ReturnSchema(Description = "The question that was asked", Example = "What is the current project deadline?")]
    public string Question { get; init; } = "";
}

/// <summary>
/// Tool that allows agents to ask questions to coworkers.
/// </summary>
[ToolContract("ask_question_to_coworker", Name = "Ask question to coworker",
    Description = "Ask a specific question to a coworker with relevant expertise",
    Category = "Collaboration")]
public partial class AskQuestionTool : ToolBase<AskQuestionRequest, AskQuestionResponse>, ITool
{
    private readonly IAgentCommunicationService _communicationService;
    private readonly AgentId _currentAgentId;
    private readonly Func<string, AgentId?> _findAgentByRole;

    /// <summary>Initializes a new instance of <see cref="AskQuestionTool"/>.</summary>
    /// <param name="communicationService">The agent communication service.</param>
    /// <param name="currentAgentId">The ID of the agent using this tool.</param>
    /// <param name="findAgentByRole">A function to look up an agent ID by role name.</param>
    /// <param name="logger">Optional logger.</param>
    public AskQuestionTool(
        IAgentCommunicationService communicationService,
        AgentId currentAgentId,
        Func<string, AgentId?> findAgentByRole,
        ILogger<AskQuestionTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(communicationService);
        _communicationService = communicationService;
        ArgumentNullException.ThrowIfNull(currentAgentId);
        _currentAgentId = currentAgentId;
        ArgumentNullException.ThrowIfNull(findAgentByRole);
        _findAgentByRole = findAgentByRole;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(AskQuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
            return "Question is required";

        if (string.IsNullOrWhiteSpace(request.CoworkerRole))
            return "Coworker role is required";

        return null;
    }

    /// <inheritdoc />
    protected override Task<AskQuestionResponse> ExecuteTypedAsync(
        AskQuestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteTypedCoreAsync();

        async Task<AskQuestionResponse> ExecuteTypedCoreAsync()
        {
            // Find the target agent by role
            var targetAgentId = _findAgentByRole(request.CoworkerRole)
                ?? throw new InvalidOperationException($"Could not find agent with role: {request.CoworkerRole}");

            // Create and send the question message
            var message = new AgentMessage(
                From: _currentAgentId,
                To: targetAgentId,
                Type: MessageType.Question,
                Content: request.Question,
                Metadata: new Dictionary<string, object>
                {
                    ["context"] = request.Context,
                    ["expects_response"] = true
                }
            );

            await _communicationService.SendMessageAsync(message).ConfigureAwait(false);

            LogAskedQuestion(targetAgentId, request.CoworkerRole);

            return new AskQuestionResponse
            {
                Message = $"Question sent to {request.CoworkerRole}. Awaiting response.",
                TargetAgentId = targetAgentId.ToString(),
                Question = request.Question
            };
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Asked question to {TargetAgent} (role: {Role})")]
    private partial void LogAskedQuestion(AgentId targetAgent, string role);
}
