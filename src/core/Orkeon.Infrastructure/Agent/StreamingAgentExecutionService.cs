using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Tools.Abstractions.Adapters;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Constants.Agent;

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

    /// <summary>Initializes a new instance of <see cref="StreamingAgentExecutionService"/>.</summary>
    /// <param name="chatClient">The Microsoft.Extensions.AI chat client used for streaming.</param>
    /// <param name="tools">The tools available to agents.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="fileSystemService">VFS service for injecting mount info into agent prompts.</param>
    public StreamingAgentExecutionService(
        IChatClient chatClient,
        IEnumerable<IBaseTool> tools,
        ILogger<StreamingAgentExecutionService> logger,
        IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _tools = tools ?? [];
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
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

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildSystemPrompt(agent)),
                new(ChatRole.User, BuildUserPrompt(task, context))
            };

            var (options, availableTools) = BuildStreamingChatOptions(agent);
            var maxIterations = agent.MaxIterations > 0 ? agent.MaxIterations : AgentDefaults.MaxIterations;

            for (int i = 0; i < maxIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (fullResponse, toolCallThoughts) = await ProcessStreamingIterationAsync(
                    messages, options, availableTools, cancellationToken).ConfigureAwait(false);

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

    private (ChatOptions options, List<IBaseTool> availableTools) BuildStreamingChatOptions(DomainAgent agent)
    {
        var agentToolNames = agent.Tools.Select(t => t.ToString()).ToHashSet();
        var availableTools = _tools.Where(t => agentToolNames.Contains(t.Name)).ToList();
        var aiTools = availableTools.Count > 0
            ? BaseToolToAIFunctionAdapter.ToAITools(availableTools)
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
                functionCalls, availableTools, messages, cancellationToken).ConfigureAwait(false);
            thoughts.AddRange(toolThoughts);
        }

        return (fullResponse.ToString(), thoughts);
    }

    private async Task<List<AgentThought>> ProcessStreamingFunctionCallsAsync(
        List<FunctionCallContent> functionCalls,
        List<IBaseTool> availableTools,
        List<ChatMessage> messages,
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

            var resultText = await ExecuteToolAsync(tool, fc, cancellationToken).ConfigureAwait(false);
            thoughts.Add(CreateThought(resultText, AgentThought.ThoughtType.ToolExecution));
            messages.Add(new ChatMessage(ChatRole.Tool, resultText));
        }

        return thoughts;
    }

    private static async Task<string> ExecuteToolAsync(
        IBaseTool tool, FunctionCallContent fc, CancellationToken cancellationToken)
    {
        var parameters = fc.Arguments?
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, object?>();

        var request = new ToolCallRequest(tool.Name, parameters);
        var result = await tool.CallAsync(request, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? result.Result?.ToString() ?? string.Empty
            : $"Error: {result.Error}";
    }

    private static AgentThought CreateThought(string content, AgentThought.ThoughtType type)
    {
        return new AgentThought(content, type, null, DateTime.UtcNow);
    }

    private string BuildSystemPrompt(DomainAgent agent)
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
