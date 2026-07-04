using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Runners;

namespace Orkeon.ConsoleApp.Commands.Qa;

/// <summary>
/// Fallback command for the Q&amp;A runner: treats any user input as a question
/// and runs it through the loaded crew, displaying the answer + metrics.
/// </summary>
internal sealed partial class AskQuestionCommand : IInteractiveCommand
{
    private readonly ILogger<AskQuestionCommand> _logger;

    public AskQuestionCommand(ILogger<AskQuestionCommand> logger)
    {
        _logger = logger;
    }

    public string Name => "ask";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Ask the loaded crew a question (default action — no need to type 'ask').";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any crew/orchestration failure is converted to an error message so one failed question cannot crash the interactive REPL. Cancellation is rethrown.")]
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var runner = context.Scope.GetRequiredService<QaRunner>();
            var configPath = runner.CrewConfigPath
                ?? throw new InvalidOperationException("QaRunner not started — CrewConfigPath is null.");

            var question = context.RawInput.Trim();

            LogInteractiveQuestion(question);

            try
            {
                var factory = context.Scope.GetRequiredService<ICrewFactory>();
                var crew = await factory.CreateFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);

                var orchestrator = context.Scope.GetRequiredService<ICrewOrchestrationService>();
                var crewInput = CrewInput.WithStringVariables(
                    initialContext: question,
                    variables: new Dictionary<string, string> { ["question"] = question });

                context.Console.WriteLine("  Processing...");
                var output = await orchestrator.KickoffAsync(crew.Id, crewInput, cancellationToken).ConfigureAwait(false);

                context.Console.WriteLine("");
                context.Console.WriteLine("[Answer]");
                context.Console.WriteLine(output.FinalOutput);
                // TokensUsed is null when no telemetry was measured (R10.8) — say so
                // instead of displaying a fabricated "0 tokens".
                var tokensLabel = output.TokensUsed is { } usage
                    ? FormattableString.Invariant($"{usage.TotalTokens} tokens")
                    : "tokens not measured";
                context.Console.WriteLine(
                    FormattableString.Invariant($"  ({output.Duration.TotalSeconds:F1}s — {tokensLabel})"));
                context.Console.WriteLine("");

                return CommandResult.Continue();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogQuestionFailed(ex);
                return CommandResult.Continue($"Error: {ex.Message}");
            }
        }
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Interactive Q&A: {Question}")]
    private partial void LogInteractiveQuestion(string question);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to process question")]
    private partial void LogQuestionFailed(Exception ex);
}
