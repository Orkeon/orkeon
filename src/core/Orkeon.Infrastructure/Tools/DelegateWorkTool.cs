using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tools.Abstractions.Base;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Infrastructure.Tools;

// ── Request / Response records ────────────────────────────────────────
/// <summary>Request parameters for the delegate work tool.</summary>
public record DelegateWorkRequest
{
    /// <summary>Gets the task description to delegate.</summary>
    [FieldSchema(Description = "The task description to delegate", Example = "Write unit tests for the authentication module")]
    public string Task { get; init; } = "";

    /// <summary>Gets the additional context for the task.</summary>
    [FieldSchema(Description = "Additional context for the task", Example = "Use xUnit with Moq for mocking dependencies")]
    public string Context { get; init; } = "";

    /// <summary>Gets the role of the coworker to delegate to.</summary>
    [FieldSchema(Description = "The role of the coworker to delegate to", Example = "senior_developer")]
    public string CoworkerRole { get; init; } = "";

    /// <summary>Gets the expected output format description.</summary>
    [FieldSchema(Description = "Description of expected output format. If empty, uses task description", IsRequired = false)]
    public string ExpectedOutput { get; init; } = "";

    /// <summary>Gets whether to execute synchronously and return the result.</summary>
    [FieldSchema(Description = "If true, executes synchronously and returns the result. If false (default), fire-and-forget", IsRequired = false)]
    public bool WaitForResult { get; init; }
}

/// <summary>Response from the delegate work tool.</summary>
public record DelegateWorkResponse
{
    /// <summary>Gets the confirmation message with delegation details.</summary>
    [ReturnSchema(Description = "Confirmation message with delegation details", Example = "Task successfully delegated to senior_developer")]
    public string Message { get; init; } = "";

    /// <summary>Gets the ID of the agent the work was delegated to.</summary>
    [ReturnSchema(Description = "ID of the agent the work was delegated to", Example = "agent-dev-042")]
    public string TargetAgentId { get; init; } = "";

    /// <summary>Gets the description of the delegated task.</summary>
    [ReturnSchema(Description = "Description of the delegated task", Example = "Write unit tests for the authentication module")]
    public string TaskDescription { get; init; } = "";

    /// <summary>Gets whether the delegated task succeeded (only set when wait_for_result=true).</summary>
    [ReturnSchema(Description = "Whether the delegated task succeeded (only set when wait_for_result=true)")]
    public bool Success { get; init; }

    /// <summary>Gets the full output from the target agent (only set when wait_for_result=true).</summary>
    [ReturnSchema(Description = "Full output from the target agent (only set when wait_for_result=true)")]
    public string Result { get; init; } = "";

    /// <summary>Gets the execution time in milliseconds (only set when wait_for_result=true).</summary>
    [ReturnSchema(Description = "Execution time in milliseconds (only set when wait_for_result=true)")]
    public long ExecutionTimeMs { get; init; }

    /// <summary>Gets the error message if the task failed (only set when wait_for_result=true).</summary>
    [ReturnSchema(Description = "Error message if the task failed (only set when wait_for_result=true)")]
    public string Error { get; init; } = "";

    /// <summary>Gets the list of tools used by the target agent (only set when wait_for_result=true).</summary>
    [ReturnSchema(Description = "List of tools used by the target agent (only set when wait_for_result=true)")]
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
}

/// <summary>
/// Tool that allows agents to delegate work to coworkers.
/// </summary>
[ToolContract("delegate_work_to_coworker", Name = "Delegate work to coworker",
    Description = "Delegate a task to a coworker with specific expertise",
    Category = "Collaboration")]
public partial class DelegateWorkTool : ToolBase<DelegateWorkRequest, DelegateWorkResponse>, ITool
{
    private readonly IAgentCommunicationService _communicationService;
    private readonly AgentId _currentAgentId;
    private readonly Func<string, AgentId?> _findAgentByRole;
    private readonly IAgentExecutionService? _agentExecutionService;
    private readonly Func<AgentId, DomainAgent?>? _findAgentById;
    private readonly Func<SimpleExecutionContext?>? _contextSupplier;
    private readonly AgentExecutionBudget? _budget;

    /// <summary>Initializes a new instance of <see cref="DelegateWorkTool"/> (fire-and-forget only).</summary>
    /// <param name="communicationService">The agent communication service.</param>
    /// <param name="currentAgentId">The ID of the agent using this tool.</param>
    /// <param name="findAgentByRole">A function to look up an agent ID by role name.</param>
    /// <param name="logger">Optional logger.</param>
    public DelegateWorkTool(
        IAgentCommunicationService communicationService,
        AgentId currentAgentId,
        Func<string, AgentId?> findAgentByRole,
        ILogger<DelegateWorkTool>? logger = null)
        : this(communicationService, currentAgentId, findAgentByRole,
               null, null, null, logger) { }

    /// <summary>Initializes a new instance of <see cref="DelegateWorkTool"/> with full synchronous delegation support.</summary>
    /// <param name="communicationService">The agent communication service.</param>
    /// <param name="currentAgentId">The ID of the agent using this tool.</param>
    /// <param name="findAgentByRole">A function to look up an agent ID by role name.</param>
    /// <param name="agentExecutionService">Optional agent execution service for synchronous delegation.</param>
    /// <param name="findAgentById">Optional function to resolve an agent entity by ID.</param>
    /// <param name="contextSupplier">Optional function to supply the current execution context.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="budget">Optional execution budget for recursive delegation control.</param>
    public DelegateWorkTool(
        IAgentCommunicationService communicationService,
        AgentId currentAgentId,
        Func<string, AgentId?> findAgentByRole,
        IAgentExecutionService? agentExecutionService,
        Func<AgentId, DomainAgent?>? findAgentById,
        Func<SimpleExecutionContext?>? contextSupplier,
        ILogger<DelegateWorkTool>? logger = null,
        AgentExecutionBudget? budget = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(communicationService);
        _communicationService = communicationService;
        ArgumentNullException.ThrowIfNull(currentAgentId);
        _currentAgentId = currentAgentId;
        ArgumentNullException.ThrowIfNull(findAgentByRole);
        _findAgentByRole = findAgentByRole;
        _agentExecutionService = agentExecutionService;
        _findAgentById = findAgentById;
        _contextSupplier = contextSupplier;
        _budget = budget;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DelegateWorkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Task))
            return "Task description is required";

        if (string.IsNullOrWhiteSpace(request.CoworkerRole))
            return "Coworker role is required";

        if (request.WaitForResult && _agentExecutionService is null)
            return "Synchronous delegation is not available: IAgentExecutionService not configured";

        return null;
    }

    /// <inheritdoc />
    protected override Task<DelegateWorkResponse> ExecuteTypedAsync(
        DelegateWorkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<DelegateWorkResponse> ExecuteTypedCoreAsync()
        {
            // Record delegation against budget if available (enables recursive depth control)
            _budget?.RecordDelegation();

            // Find the target agent by role
            var targetAgentId = _findAgentByRole(request.CoworkerRole)
                ?? throw new InvalidOperationException($"Could not find agent with role: {request.CoworkerRole}");

            if (request.WaitForResult)
                return await ExecuteSynchronousAsync(request, targetAgentId, cancellationToken)
                    .ConfigureAwait(false);

            // Create and send the delegation message
            var message = new AgentMessage(
                From: _currentAgentId,
                To: targetAgentId,
                Type: MessageType.Task,
                Content: request.Task,
                Metadata: new Dictionary<string, object>
                {
                    ["context"] = request.Context,
                    ["delegated"] = true
                }
            );

            await _communicationService.SendMessageAsync(message).ConfigureAwait(false);

            LogDelegatedTask(targetAgentId, request.CoworkerRole);

            return new DelegateWorkResponse
            {
                Message = $"Task successfully delegated to {request.CoworkerRole}",
                TargetAgentId = targetAgentId.ToString(),
                TaskDescription = request.Task,
                Success = true
            };
        }
    }

    private async Task<DelegateWorkResponse> ExecuteSynchronousAsync(
        DelegateWorkRequest request,
        AgentId targetAgentId,
        CancellationToken cancellationToken)
    {
        if (_agentExecutionService is null)
            throw new InvalidOperationException(
                "Synchronous delegation is not available: IAgentExecutionService not configured");

        if (_findAgentById is null)
            throw new InvalidOperationException(
                "Synchronous delegation is not available: agent resolver not configured");

        var targetAgent = _findAgentById(targetAgentId)
            ?? throw new InvalidOperationException(
                $"Could not load agent entity for id: {targetAgentId}");

        var expectedOutput = string.IsNullOrWhiteSpace(request.ExpectedOutput)
            ? request.Task
            : request.ExpectedOutput;

        var task = CrewTask.Create(
            TaskDescription.From(request.Task),
            ExpectedOutput.From(expectedOutput));

        var parentContext = _contextSupplier?.Invoke();
        var childVariables = parentContext?.Variables is { } vars
            ? new Dictionary<string, string>(vars)
            : [];

        if (!string.IsNullOrWhiteSpace(request.Context))
            childVariables["delegation_context"] = request.Context;

        var childContext = new SimpleExecutionContext(
            parentContext?.CrewId ?? CrewId.Create(),
            childVariables,
            parentContext?.Memory ?? Orkeon.Application.Context.NullMemoryScope.Instance,
            parentContext?.PreviousOutputs ?? [],
            cancellationToken);

        LogSynchronousDelegationStarted(targetAgentId, request.CoworkerRole);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await _agentExecutionService
            .ExecuteTaskAsync(targetAgent, task, childContext, cancellationToken)
            .ConfigureAwait(false);

        sw.Stop();
        LogSynchronousDelegationCompleted(targetAgentId, result.Success, sw.Elapsed);

        return new DelegateWorkResponse
        {
            Message = result.Success
                ? $"Task executed by {request.CoworkerRole}"
                : $"Task failed on {request.CoworkerRole}: {result.Error}",
            TargetAgentId = targetAgentId.ToString(),
            TaskDescription = request.Task,
            Success = result.Success,
            Result = result.Output,
            ExecutionTimeMs = sw.ElapsedMilliseconds,
            Error = result.Error ?? "",
            ToolsUsed = result.ToolsUsed.Select(t => t.ToolName).ToArray()
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Delegated task to {TargetAgent} (role: {Role})")]
    private partial void LogDelegatedTask(AgentId targetAgent, string role);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting synchronous delegation to agent {TargetAgentId} (role: {CoworkerRole})")]
    private partial void LogSynchronousDelegationStarted(AgentId targetAgentId, string coworkerRole);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Synchronous delegation to agent {TargetAgentId} completed (success: {Success}) in {Duration}")]
    private partial void LogSynchronousDelegationCompleted(AgentId targetAgentId, bool success, TimeSpan duration);
}
