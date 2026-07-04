using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Task;
using System.Text;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Groups all parameters needed for output validation and retry.
/// </summary>
internal sealed record OutputValidationRequest(
    string Output,
    OutputValidationContext? ValidationContext,
    CrewTask Task,
    DomainAgent Agent,
    string SystemPrompt,
    string UserPrompt,
    List<Domain.Tools.ToolUsage> ToolsUsed);

/// <summary>
/// Validates LLM output against the task's expected format and coordinates correction
/// retries (via the IChatClient loop when available, otherwise a single-shot legacy call).
/// Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal sealed class OutputValidationCoordinator
{
    private readonly ILogger _logger;
    private readonly IOutputValidationPipeline? _validationPipeline;
    private readonly IOutputParserFactory? _parserFactory;
    private readonly IBasicLlmProvider _llmProvider;
    private readonly ChatClientAgentLoop? _chatLoop;

    internal OutputValidationCoordinator(
        ILogger logger,
        IOutputValidationPipeline? validationPipeline,
        IOutputParserFactory? parserFactory,
        IBasicLlmProvider llmProvider,
        ChatClientAgentLoop? chatLoop)
    {
        _logger = logger;
        _validationPipeline = validationPipeline;
        _parserFactory = parserFactory;
        _llmProvider = llmProvider;
        _chatLoop = chatLoop;
    }

    /// <summary>
    /// Builds an OutputValidationContext from task properties if the task has output format expectations.
    /// Returns null if no validation is configured.
    /// </summary>
    internal static OutputValidationContext? BuildOutputValidationContext(CrewTask task)
    {
        // Check if task has OutputJson or expected format hints
        if (task.OutputJson != null)
        {
            return new OutputValidationContext(
                ExpectedFormat: Interfaces.Ports.OutputFormat.Json,
                Schema: task.OutputJson,
                ExpectedType: task.OutputPydantic);
        }

        return null;
    }

    /// <summary>
    /// Validates LLM output and optionally retries with correction prompts.
    /// If no validation pipeline is configured, returns the output as-is.
    /// </summary>
    internal async System.Threading.Tasks.Task<(string output, object? structuredOutput)> ValidateAndParseOutputAsync(
        OutputValidationRequest request,
        int maxOutputRetries,
        int defaultMaxIterations,
        CancellationToken cancellationToken)
    {
        if (_validationPipeline == null || request.ValidationContext == null)
            return (request.Output, null);

        var currentOutput = request.Output;

        for (int retry = 0; retry <= maxOutputRetries; retry++)
        {
            var pipelineResult = await _validationPipeline.ValidateAsync(currentOutput, request.ValidationContext, cancellationToken).ConfigureAwait(false);

            if (pipelineResult.IsValid)
                return (currentOutput, TryParseStructuredOutput(currentOutput, request.ValidationContext));

            if (retry >= maxOutputRetries)
            {
                LogRetryExhausted(request.Task, pipelineResult, maxOutputRetries);
                break;
            }

            LogRetryAttempt(request.Task, retry, pipelineResult, maxOutputRetries);
            var correctionContext = new CorrectionExecutionContext(
                request.Agent, request.Task, request.SystemPrompt, request.ToolsUsed, defaultMaxIterations);
            currentOutput = await RetryWithCorrectionAsync(
                pipelineResult, request.ValidationContext, correctionContext, cancellationToken).ConfigureAwait(false);
        }

        return (currentOutput, null);
    }

    private object? TryParseStructuredOutput(string currentOutput, OutputValidationContext validationContext)
    {
        if (_parserFactory == null || validationContext.ExpectedFormat != Interfaces.Ports.OutputFormat.Json)
            return null;

        var parser = _parserFactory.CreateParser(validationContext.ExpectedFormat);
        if (validationContext.ExpectedType == null)
            return null;

        parser.TryParse(currentOutput, validationContext.ExpectedType, out var structuredOutput);
        return structuredOutput;
    }

    private void LogRetryExhausted(CrewTask task, OutputPipelineResult pipelineResult, int maxOutputRetries)
    {
        ExecutionLog.LogOutputValidationExhausted(_logger, maxOutputRetries, task.Id, pipelineResult.CombinedErrorMessage ?? "Unknown error");
    }

    private void LogRetryAttempt(CrewTask task, int retry, OutputPipelineResult pipelineResult, int maxOutputRetries)
    {
        ExecutionLog.LogOutputValidationRetry(_logger, task.Id, retry + 1, maxOutputRetries, pipelineResult.CombinedErrorMessage ?? "Unknown error");
    }

    /// <summary>
    /// Cohesive execution inputs for a single correction attempt: the agent, task, system prompt,
    /// running tool-usage log, and default iteration cap. Mirrors what
    /// <see cref="ChatClientAgentLoop.ExecuteAsync"/> consumes (minus the user prompt, built per attempt).
    /// </summary>
    private sealed record CorrectionExecutionContext(
        DomainAgent Agent,
        CrewTask Task,
        string SystemPrompt,
        List<Domain.Tools.ToolUsage> ToolsUsed,
        int DefaultMaxIterations);

    private async System.Threading.Tasks.Task<string> RetryWithCorrectionAsync(
        OutputPipelineResult pipelineResult,
        OutputValidationContext validationContext,
        CorrectionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var correctionPrompt = BuildCorrectionPrompt(pipelineResult, validationContext);

        if (_chatLoop != null)
        {
            var correctionResult = await _chatLoop.ExecuteAsync(
                context.Agent, context.Task, context.SystemPrompt, correctionPrompt,
                context.ToolsUsed, context.DefaultMaxIterations, cancellationToken).ConfigureAwait(false);
            return correctionResult.Output;
        }

        var prompt = $"{context.SystemPrompt}\n\n{correctionPrompt}";
        var correctionConfig = Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve(
            baseConfig: context.Agent.LlmConfig ?? Domain.SharedKernel.ValueObjects.LlmConfig.Default(),
            taskOverride: context.Task.LlmOverride,
            callOverride: null);
        return await _llmProvider.ChatAsync(prompt, correctionConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a correction prompt for the LLM to fix validation errors.
    /// </summary>
    private static string BuildCorrectionPrompt(
        OutputPipelineResult validationResult,
        OutputValidationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your previous output had validation errors:");

        foreach (var result in validationResult.Results.Where(r => !r.IsValid))
        {
            sb.AppendLine(FormattableString.Invariant($"- {result.ErrorMessage}"));
            if (!string.IsNullOrEmpty(result.SuggestedFix))
            {
                sb.AppendLine(FormattableString.Invariant($"  Fix: {result.SuggestedFix}"));
            }
        }

        sb.AppendLine();
        sb.AppendLine(FormattableString.Invariant($"Please fix these issues and respond again in {context.ExpectedFormat} format."));

        if (context.RequiredFields?.Count > 0)
        {
            sb.AppendLine(FormattableString.Invariant($"Required fields: {string.Join(", ", context.RequiredFields)}"));
        }

        return sb.ToString();
    }
}
