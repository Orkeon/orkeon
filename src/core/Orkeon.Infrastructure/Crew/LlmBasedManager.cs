using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Execution;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Constants.Scoring;

namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// LLM-based manager agent that assigns tasks and reviews outputs using a language model.
/// </summary>
public partial class LlmBasedManager : IManagerAgent
{
    private readonly ILogger<LlmBasedManager> _logger;
    private readonly IBasicLlmProvider _llmProvider;
    private readonly IChatClient? _chatClient;

    /// <summary>Initializes a new instance of <see cref="LlmBasedManager"/>.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="llmProvider">The LLM provider for task assignment decisions.</param>
    public LlmBasedManager(
        ILogger<LlmBasedManager> logger,
        IBasicLlmProvider llmProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
    }

    /// <summary>
    /// Constructor that accepts IChatClient for the new M.E.AI integration path.
    /// When both are provided, IChatClient is preferred over IBasicLlmProvider.
    /// </summary>
    public LlmBasedManager(
        ILogger<LlmBasedManager> logger,
        IBasicLlmProvider llmProvider,
        IChatClient chatClient)
        : this(logger, llmProvider)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
    }

    /// <inheritdoc />
    public Task<TaskAssignment> AssignTaskAsync(
        CrewTask task,
        IReadOnlyList<DomainAgent> availableAgents,
        SimpleExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(task);
        // Validate inputs
        ArgumentNullException.ThrowIfNull(availableAgents);

        if (availableAgents.Count == 0)
        {
            throw new InvalidOperationException("No available agents to assign task");
        }

        return AssignTaskCoreAsync(task, availableAgents, context);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Manager fault barrier: any LLM/parse failure during assignment is logged and converted into a deterministic fallback assignment so the crew can still proceed (cancellation is rethrown).")]
    private async Task<TaskAssignment> AssignTaskCoreAsync(
        CrewTask task,
        IReadOnlyList<DomainAgent> availableAgents,
        SimpleExecutionContext context)
    {
        LogManagerAssigningTask(task.Description.Value);

        var prompt = BuildAssignmentPrompt(task, availableAgents, context);

        try
        {
            var response = await SendPromptAsync(prompt, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            var assignment = ParseAssignmentResponse(response, task, availableAgents);

            LogTaskAssignedToAgentWith(assignment.TaskId, assignment.AssignedAgent, assignment.Reason);

            return assignment;
        }
        catch (OperationCanceledException)
        {
            // Re-throw cancellation exceptions
            throw;
        }
        catch (Exception ex)
        {
            LogErrorDuringTaskAssignmentFor(ex, task.Id);

            // Fallback to first available agent
            var fallbackAgent = availableAgents[0];
            return new TaskAssignment(
                task.Id,
                fallbackAgent.Id,
                "Assigned by fallback due to error in LLM assignment",
                DateTime.UtcNow
            );
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Manager review fault barrier: any LLM/parse failure is logged and defaults the review to approval so a transient model error does not stall the pipeline.")]
    public Task<bool> ReviewOutputAsync(
        TaskOutput output,
        CrewTask originalTask)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(originalTask);
        return ReviewOutputCoreAsync();

        async Task<bool> ReviewOutputCoreAsync()
        {
            LogManagerReviewingOutputForTask(originalTask.Id);

            var prompt = BuildReviewPrompt(output, originalTask);

            try
            {
                var response = await SendPromptAsync(prompt, CancellationToken.None).ConfigureAwait(false);
                var approved = ParseReviewResponse(response);

                LogOutputReviewForTask(originalTask.Id, approved ? "Approved" : "Rejected");

                return approved;
            }
            catch (Exception ex)
            {
                LogErrorDuringOutputReviewFor(ex, originalTask.Id);

                // Default to approval on error
                return true;
            }
        }
    }

    private static string BuildAssignmentPrompt(CrewTask task, IReadOnlyList<DomainAgent> agents, SimpleExecutionContext? context = null)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are a project manager responsible for assigning tasks to team members.");
        prompt.AppendLine();
        prompt.AppendLine($"Task to assign:");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Description: {task.Description}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Expected Output: {task.ExpectedOutput}");

        if (task.Context != null && task.Context.Count > 0)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"Context: {JsonSerializer.Serialize(task.Context)}");
        }
        if (context != null && context.Variables.Count > 0)
        {
            prompt.AppendLine($"Context: Additional context information");
        }

        prompt.AppendLine();
        prompt.AppendLine("Available team members:");

        foreach (var agent in agents)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- ID: {agent.Id}");
            prompt.AppendLine(CultureInfo.InvariantCulture, $"  Role: {agent.Role.Value}");
            prompt.AppendLine(CultureInfo.InvariantCulture, $"  Goal: {agent.Goal.Value}");

            if (agent.Backstory is not null)
            {
                prompt.AppendLine(CultureInfo.InvariantCulture, $"  Backstory: {agent.Backstory}");
            }

            if (agent.Tools.Count > 0)
            {
                prompt.AppendLine(CultureInfo.InvariantCulture, $"  Tools: {string.Join(", ", agent.Tools.Select(t => t.Name))}");
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("Based on the task requirements and agent capabilities, select the most suitable agent.");
        prompt.AppendLine("Respond in JSON format:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"agent_id\": \"<selected_agent_id>\",");
        prompt.AppendLine("  \"reason\": \"<brief explanation of why this agent was chosen>\"");
        prompt.AppendLine("}");

        return prompt.ToString();
    }

    private static string BuildReviewPrompt(TaskOutput output, CrewTask originalTask)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are a project manager reviewing the output of a completed task.");
        prompt.AppendLine();
        prompt.AppendLine($"Original Task:");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Description: {originalTask.Description}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Expected Output: {originalTask.ExpectedOutput}");
        prompt.AppendLine();
        prompt.AppendLine($"Actual Output:");
        prompt.AppendLine(output.Content);
        prompt.AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Execution Time: {output.ExecutionTime}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Success Status: {output.Success}");

        if (output.ToolsUsed?.Count > 0)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"Tools Used: {output.ToolsUsed.Count}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Review the output and determine if it meets the expected requirements.");
        prompt.AppendLine("Consider:");
        prompt.AppendLine("1. Does the output address the task description?");
        prompt.AppendLine("2. Does it meet the expected output criteria?");
        prompt.AppendLine("3. Is the quality acceptable?");
        prompt.AppendLine();
        prompt.AppendLine("Respond in JSON format:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"approved\": true/false,");
        prompt.AppendLine("  \"feedback\": \"<optional feedback for improvement>\"");
        prompt.AppendLine("}");

        return prompt.ToString();
    }

    private TaskAssignment ParseAssignmentResponse(
        string response,
        CrewTask task,
        IReadOnlyList<DomainAgent> agents)
    {
        // Try direct JSON parsing, then embedded JSON extraction
        var assignment = TryExtractAgentAssignment(response, task, agents);
        if (assignment != null)
            return assignment;

        // Fallback: Simple heuristic based on role matching
        var bestAgent = agents
            .OrderByDescending(a => CalculateAgentScore(a, task))
            .First();

        return new TaskAssignment(
            task.Id,
            bestAgent.Id,
            "Selected based on role and capability matching",
            DateTime.UtcNow
        );
    }

    /// <summary>
    /// Attempts to extract an agent assignment from a JSON response.
    /// Tries parsing the full response as JSON first, then falls back to extracting an embedded JSON block.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant LLM-response parsing: any malformed/unexpected model output is logged and treated as 'no assignment extracted' (null) so the caller can fall back to heuristic matching.")]
    private TaskAssignment? TryExtractAgentAssignment(
        string response,
        CrewTask task,
        IReadOnlyList<DomainAgent> agents)
    {
        // First, try to parse the entire response as JSON
        var jsonToParse = response;
        try
        {
            using var doc = JsonDocument.Parse(jsonToParse);
            return TryBuildAssignmentFromJson(doc.RootElement, task, agents);
        }
        catch (JsonException)
        {
            // If direct parsing fails, try to extract an embedded JSON block
        }
        catch (Exception ex)
        {
            LogFailedToParseAssignmentResponse(ex);
            return null;
        }

        // Try extracting embedded JSON block
        var extractedJson = TryExtractJsonBlock(response);
        if (extractedJson == null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(extractedJson);
            return TryBuildAssignmentFromJson(doc.RootElement, task, agents);
        }
        catch (Exception ex)
        {
            LogFailedToParseAssignmentResponse2(ex);
            return null;
        }
    }

    /// <summary>
    /// Extracts the first balanced JSON block (matching braces) from a string.
    /// Returns null if no valid JSON block is found.
    /// </summary>
    private static string? TryExtractJsonBlock(string response)
    {
        var jsonStart = response.IndexOf('{', StringComparison.Ordinal);
        if (jsonStart < 0)
            return null;

        int braceCount = 0;
        int jsonEnd = -1;
        for (int i = jsonStart; i < response.Length; i++)
        {
            if (response[i] == '{')
                braceCount++;
            else if (response[i] == '}')
            {
                braceCount--;
                if (braceCount == 0)
                {
                    jsonEnd = i;
                    break;
                }
            }
        }

        if (jsonEnd <= jsonStart)
            return null;

        return response.Substring(jsonStart, jsonEnd - jsonStart + 1);
    }

    /// <summary>
    /// Builds a TaskAssignment from a parsed JSON element containing agent_id and optional reason.
    /// Returns null if the JSON does not contain a valid agent_id matching an available agent.
    /// </summary>
    private static TaskAssignment? TryBuildAssignmentFromJson(
        JsonElement root,
        CrewTask task,
        IReadOnlyList<DomainAgent> agents)
    {
        if (!root.TryGetProperty("agent_id", out var agentIdElement))
            return null;

        var agentIdStr = agentIdElement.GetString();
        if (string.IsNullOrEmpty(agentIdStr))
            return null;

        string reason = "No reason provided";
        if (root.TryGetProperty("reason", out var reasonElement))
        {
            var reasonText = reasonElement.GetString();
            if (!string.IsNullOrEmpty(reasonText))
                reason = reasonText;
        }

        var agent = agents.FirstOrDefault(a => a.Id.ToString() == agentIdStr);
        if (agent == null)
            return null;

        return new TaskAssignment(
            task.Id,
            agent.Id,
            reason,
            DateTime.UtcNow
        );
    }

    private bool ParseReviewResponse(string response)
    {
        var jsonResult = TryParseApprovalFromJson(response);
        if (jsonResult.HasValue)
            return jsonResult.Value;

        return ParseApprovalFromKeywords(response);
    }

    private bool? TryParseApprovalFromJson(string response)
    {
        // Try direct JSON parsing
        var result = TryExtractApprovedField(response);
        if (result.HasValue)
            return result;

        // Try extracting embedded JSON block
        var extractedJson = TryExtractJsonBlock(response);
        if (extractedJson != null)
        {
            result = TryExtractApprovedField(extractedJson);
            if (result.HasValue)
                return result;
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant LLM-response parsing: any malformed model output is logged and treated as 'no approval field' (null) so the caller falls back to keyword heuristics.")]
    private bool? TryExtractApprovedField(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("approved", out var approvedElement))
                return approvedElement.GetBoolean();
        }
        catch (JsonException)
        {
            // Not valid JSON
        }
        catch (Exception ex)
        {
            LogFailedToParseReviewResponse(ex);
        }
        return null;
    }

    private static bool ParseApprovalFromKeywords(string response)
    {
#pragma warning disable CA1308 // lowercase is the required normalized form scanned for substring keywords
        var lowerResponse = response.ToLowerInvariant();
#pragma warning restore CA1308

        if (ContainsRejectionKeyword(lowerResponse))
            return false;

        return ContainsApprovalKeyword(lowerResponse);
    }

    private static readonly string[] s_rejectionKeywords =
        ["not acceptable", "rejected", "reject", "insufficient", "not approved", "is not", "needs more"];

    private static readonly string[] s_approvalKeywords =
        ["approved", "approve", "accept", "satisfactory", "meets requirements", "is approved"];

    private static bool ContainsRejectionKeyword(string text) =>
        s_rejectionKeywords.Any(k => text.Contains(k, StringComparison.Ordinal));

    private static bool ContainsApprovalKeyword(string text) =>
        s_approvalKeywords.Any(k => text.Contains(k, StringComparison.Ordinal));

    /// <summary>
    /// Sends a prompt to the LLM, preferring IChatClient when available.
    /// </summary>
    private async Task<string> SendPromptAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_chatClient != null)
        {
            var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
            var chatResponse = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
            return chatResponse.Text ?? string.Empty;
        }

        return await _llmProvider.ChatAsync(prompt, null, cancellationToken).ConfigureAwait(false);
    }

    private static double CalculateAgentScore(DomainAgent agent, CrewTask task)
    {
#pragma warning disable CA1308 // lowercase produces normalized word tokens for case-insensitive matching
        var taskWords = task.Description.ToString().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var roleWords = agent.Role.Value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var goalWords = agent.Goal.Value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
#pragma warning restore CA1308

        double score = 0;
        score += CountExactMatches(taskWords, roleWords, goalWords);
        score += CountPartialMatches(taskWords, roleWords, ScoringDefaults.ExactMatchBonus);
        score += CountPartialMatches(taskWords, goalWords, ScoringDefaults.PartialMatchWeight);
        score += agent.Tools.Count * ScoringDefaults.HistoricalMultiplier;
        if (agent.AllowDelegation) score += 3;

        return score;
    }

    private static double CountExactMatches(string[] taskWords, string[] roleWords, string[] goalWords)
    {
        double score = 0;
        foreach (var word in taskWords)
        {
            if (roleWords.Contains(word)) score += 2;
            if (goalWords.Contains(word)) score += 1;
        }
        return score;
    }

    private static double CountPartialMatches(string[] taskWords, string[] targetWords, double bonus)
    {
        double score = 0;
        foreach (var taskWord in taskWords)
        {
            if (taskWord.Length < ScoringDefaults.MinWordLengthForMatching) continue;

            foreach (var targetWord in targetWords)
            {
                if (targetWord.Contains(taskWord, StringComparison.Ordinal) || taskWord.Contains(targetWord, StringComparison.Ordinal))
                {
                    score += bonus;
                    break;
                }
            }
        }
        return score;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Manager assigning task: {TaskDescription}")]
    private partial void LogManagerAssigningTask(string taskDescription);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Task {TaskId} assigned to agent {AgentId} with reason: {Reason}")]
    private partial void LogTaskAssignedToAgentWith(TaskId taskId, AgentId agentId, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error during task assignment for task {TaskId}")]
    private partial void LogErrorDuringTaskAssignmentFor(Exception ex, TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Manager reviewing output for task: {TaskId}")]
    private partial void LogManagerReviewingOutputForTask(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Output review for task {TaskId}: {Approved}")]
    private partial void LogOutputReviewForTask(TaskId taskId, string approved);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error during output review for task {TaskId}")]
    private partial void LogErrorDuringOutputReviewFor(Exception ex, TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse assignment response as JSON")]
    private partial void LogFailedToParseAssignmentResponse(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse assignment response as JSON")]
    private partial void LogFailedToParseAssignmentResponse2(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse review response as JSON")]
    private partial void LogFailedToParseReviewResponse(Exception ex);

}
