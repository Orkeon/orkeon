using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Task;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Agent;

/// <summary>
/// Streaming agent execution service that emits AgentThought events in real-time.
/// Uses IChatClient for streaming LLM responses.
/// </summary>
public sealed partial class StreamingAgentExecutionService : IStreamingAgentExecutionService
{
    private readonly IChatClient _chatClient;
    private readonly IEnumerable<IBaseTool> _tools;
    private readonly ILogger<StreamingAgentExecutionService> _logger;
    private readonly IFileSystemService _fileSystemService;
    private readonly IToolInvocationPipeline _toolInvocation;
    private readonly IGuardianPipeline? _guardian;

    /// <summary>Initializes a new instance of <see cref="StreamingAgentExecutionService"/>.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client used for streaming.</param>
    /// <param name="tools">The tools available to agents.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="fileSystemService">VFS service for injecting mount info into agent prompts.</param>
    /// <param name="toolInvocation">The single tool-invocation point (guardian, sanitizer, audit — GAP-09);
    /// null falls back to <see cref="ToolInvocationPipeline.Unguarded"/>.</param>
    /// <param name="guardian">The guardian whose input phase screens the user prompt; null runs no input check.</param>
    public StreamingAgentExecutionService(
        IChatClient chatClient,
        IEnumerable<IBaseTool> tools,
        ILogger<StreamingAgentExecutionService> logger,
        IFileSystemService fileSystemService,
        IToolInvocationPipeline? toolInvocation = null,
        IGuardianPipeline? guardian = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _tools = tools ?? [];
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
        _toolInvocation = toolInvocation ?? ToolInvocationPipeline.Unguarded;
        _guardian = guardian;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AgentThought> StreamExecutionAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);
        return StreamExecutionCoreAsync(cancellationToken);

        // [EnumeratorCancellation] belongs on the actual async-iterator (this local function), so a
        // consumer's WithCancellation(token) still flows into the stream after the S4457 split.
        async IAsyncEnumerable<AgentThought> StreamExecutionCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return CreateThought($"Starting task: {task.Description}", AgentThought.ThoughtType.Reasoning);

            var userPrompt = BuildUserPrompt(task, context);
            var caller = new ToolInvocationCaller(agent.Id.ToString(), agent.Role.Value, task.Id.ToString(), context.CrewId.ToString());

            // Input phase, as on the non-streaming path: a blocked prompt never reaches the model.
            var inputVerdict = await CheckInputAsync(caller, userPrompt, cancellationToken).ConfigureAwait(false);
            if (inputVerdict is { IsAllowed: false })
            {
                yield return CreateThought($"Blocked by Guardian (input): {inputVerdict.Reason}", AgentThought.ThoughtType.Error);
                yield break;
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildSystemPrompt(agent, task)),
                new(ChatRole.User, userPrompt)
            };

            var (options, availableTools) = BuildStreamingChatOptions(agent, task, caller);
            var maxIterations = agent.MaxIterations > 0 ? agent.MaxIterations : AgentDefaults.MaxIterations;

            for (int i = 0; i < maxIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Opened around the awaited turn, not across the yields below: an iterator
                // resumes from a yield in its consumer's context, where the scope is gone.
                (string fullResponse, List<AgentThought> toolCallThoughts) turn;
                using (LlmUsageScope.Begin(
                    LlmUsageOperations.Agent,
                    crewId: context.CrewId.ToString(),
                    agentId: agent.Role.Value,
                    taskId: task.Id.ToString()))
                {
                    turn = await ProcessStreamingIterationAsync(
                        messages, options, availableTools, caller, cancellationToken).ConfigureAwait(false);
                }

                var (fullResponse, toolCallThoughts) = turn;

                foreach (var thought in toolCallThoughts)
                    yield return thought;

                var hadToolCalls = toolCallThoughts.Any(t =>
                    t.Type == AgentThought.ThoughtType.ToolSelection);

                if (hadToolCalls)
                    continue;

                yield return CreateThought(fullResponse, AgentThought.ThoughtType.Conclusion);
                yield break;
            }

            yield return CreateThought("Max iterations reached", AgentThought.ThoughtType.Error);
        }
    }

    private async Task<GuardResult?> CheckInputAsync(ToolInvocationCaller caller, string userPrompt, CancellationToken cancellationToken)
    {
        if (_guardian is null)
            return null;

        return await _guardian.ExecuteAsync(new GuardContext
        {
            Phase = GuardPhase.Input,
            AgentId = caller.AgentId,
            AgentRole = caller.AgentRole,
            CrewId = caller.CrewId ?? string.Empty,
            Content = userPrompt,
        }, cancellationToken).ConfigureAwait(false);
    }

    private (ChatOptions options, List<IBaseTool> availableTools) BuildStreamingChatOptions(
        DomainAgent agent, CrewTask task, ToolInvocationCaller caller)
    {
        // The same belt as the other loops: agent + task tools + human_input (GAP-07).
        var availableTools = TaskToolbelt.Compose(agent, task, _tools).ToList();
        // Invoked only by a client that runs functions itself; this loop dispatches below.
        // Either way, through the invocation pipeline.
        List<AITool>? aiTools = availableTools.Count > 0
            ? availableTools.Select(t => (AITool)AIFunctionFactory.Create(
                method: async (AIFunctionArguments args, CancellationToken ct) =>
                {
                    var outcome = await _toolInvocation.InvokeAsync(
                        new ToolInvocation(t, args.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), caller), ct).ConfigureAwait(false);
                    return outcome.ConversationText;
                },
                name: t.Name,
                description: t.Schema.Description)).ToList()
            : null;

        var options = new ChatOptions
        {
            Tools = aiTools,
            ToolMode = aiTools?.Count > 0 ? ChatToolMode.Auto : null
        };

        return (options, availableTools);
    }

    private async Task<(string fullResponse, List<AgentThought> thoughts)> ProcessStreamingIterationAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        List<IBaseTool> availableTools,
        ToolInvocationCaller caller,
        CancellationToken cancellationToken)
    {
        var fullResponse = new StringBuilder();
        var thoughts = new List<AgentThought>();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (update.Text is not null)
            {
                fullResponse.Append(update.Text);
                thoughts.Add(CreateThought(update.Text, AgentThought.ThoughtType.Reasoning));
            }

            var functionCalls = update.Contents?.OfType<FunctionCallContent>().ToList();
            if (functionCalls is not { Count: > 0 })
                continue;

            var toolThoughts = await ProcessStreamingFunctionCallsAsync(
                functionCalls, availableTools, messages, caller, cancellationToken).ConfigureAwait(false);
            thoughts.AddRange(toolThoughts);
        }

        return (fullResponse.ToString(), thoughts);
    }

    private async Task<List<AgentThought>> ProcessStreamingFunctionCallsAsync(
        List<FunctionCallContent> functionCalls,
        List<IBaseTool> availableTools,
        List<ChatMessage> messages,
        ToolInvocationCaller caller,
        CancellationToken cancellationToken)
    {
        var thoughts = new List<AgentThought>();

        foreach (var fc in functionCalls)
        {
            thoughts.Add(CreateThought($"Calling tool: {fc.Name}", AgentThought.ThoughtType.ToolSelection));

            var tool = availableTools.FirstOrDefault(t => t.Name == fc.Name);
            if (tool is null)
            {
                LogToolNotFound(fc.Name);
                continue;
            }

            var resultText = await ExecuteToolAsync(tool, fc, caller, cancellationToken).ConfigureAwait(false);
            thoughts.Add(CreateThought(resultText, AgentThought.ThoughtType.ToolExecution));
            messages.Add(new ChatMessage(ChatRole.Tool, resultText));
        }

        return thoughts;
    }

    private async Task<string> ExecuteToolAsync(
        IBaseTool tool, FunctionCallContent fc, ToolInvocationCaller caller, CancellationToken cancellationToken)
    {
        var parameters = fc.Arguments?
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, object?>();

        var outcome = await _toolInvocation.InvokeAsync(new ToolInvocation(tool, parameters, caller), cancellationToken)
            .ConfigureAwait(false);
        return outcome.ConversationText;
    }

    private static AgentThought CreateThought(string content, AgentThought.ThoughtType type)
    {
        return new AgentThought(content, type, null, DateTime.UtcNow);
    }

    private string BuildSystemPrompt(DomainAgent agent, CrewTask task)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"You are {agent.Role}, {agent.Backstory}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Your goal: {agent.Goal}");

        var mounts = _fileSystemService.GetAvailableMounts();
        if (mounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Available File Mounts");
            sb.AppendLine();
            sb.AppendLine("| Mount | Rights | Notes |");
            sb.AppendLine("|-------|--------|-------|");

            foreach (var mount in mounts)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {mount.VirtualPath} | {FormatRights(mount.DefaultRights)} | {FormatNotes(mount.DefaultRights)} |");

                foreach (var ov in mount.Overrides)
                {
                    var fullPath = $"{mount.VirtualPath.TrimEnd('/')}/{ov.RelativePath}";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {fullPath} | {FormatRights(ov.Rights)} | {FormatNotes(ov.Rights)} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("All file operations must use these virtual paths. Absolute or unmounted paths are not allowed.");
        }

        // Same renderer as the non-streaming path (AgentPromptComposer): agent-level guardrails
        // first, then the task's own, tool rules gated by the agent's tools.
        GuardrailsPromptRenderer.AppendAgentAndTaskGuardrails(sb, agent, task, TaskToolbelt.Compose(agent, task, _tools));

        return sb.ToString();
    }

    private static string FormatRights(FileAccessRights rights) => rights switch
    {
        FileAccessRights.ReadOnly => "ro",
        FileAccessRights.ReadWrite => "rw",
        FileAccessRights.ReadWriteNoDelete => "rwnd",
#pragma warning disable CA1308 // lowercase is the required display/wire form of the rights token, not a comparison normalization
        _ => rights.ToString().ToLowerInvariant()
#pragma warning restore CA1308
    };

    private static string FormatNotes(FileAccessRights rights) => rights switch
    {
        FileAccessRights.ReadOnly => "Read-only",
        FileAccessRights.ReadWrite => "Full access",
        FileAccessRights.ReadWriteNoDelete => "Read, write, create (no delete)",
        _ => rights.ToString()
    };

    private static string BuildUserPrompt(CrewTask task, SimpleExecutionContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Task: {task.Description}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Expected output: {task.ExpectedOutput}");

        // ToolCall deliverables are written by the agent itself: name the path, or the
        // agent guesses one (see AgentPromptComposer.AppendDeliverableInstruction).
        if (task.Deliverable is { Source: Orkeon.Domain.Task.ValueObjects.DeliverableSource.ToolCall, Path.Length: > 0 } deliverable)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Deliverable: write the final result to '{deliverable.Path}' using the file_write tool.");
        }

        if (context.PreviousOutputs.Count > 0)
        {
            sb.AppendLine("\nContext from previous tasks:");
            foreach (var output in context.PreviousOutputs)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {output.Content}");
            }
        }

        return sb.ToString();
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Tool {ToolName} not found")]
    private partial void LogToolNotFound(object toolName);

}
