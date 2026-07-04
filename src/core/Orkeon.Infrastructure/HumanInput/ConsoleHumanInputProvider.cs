using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;
using Orkeon.Domain.HumanInput.ValueObjects;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>
/// Console-based human input provider implementation.
/// </summary>
public sealed partial class ConsoleHumanInputProvider : IHumanInputProvider
{
    private readonly ILogger<ConsoleHumanInputProvider> _logger;

    /// <summary>Initializes a new instance of <see cref="ConsoleHumanInputProvider"/>.</summary>
    /// <param name="logger">The logger.</param>
    public ConsoleHumanInputProvider(ILogger<ConsoleHumanInputProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Presents a human input request on the console and waits for a response.
    /// </summary>
    /// <param name="request">The input request details.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The user's response, or the default value if no input is provided.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literal is an interactive console prompt.")]
    public static Task<string> RequestInputAsync(HumanInputRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequestInputCoreAsync();

        async Task<string> RequestInputCoreAsync()
        {
            Console.WriteLine($"\n[Human Input Required]\n{request.Prompt}");

            if (request.Options?.Count > 0)
            {
                for (int i = 0; i < request.Options.Count; i++)
                    Console.WriteLine($"{i + 1}. {request.Options[i]}");
            }

            Console.Write("> ");
            var input = await System.Threading.Tasks.Task.Run(() => Console.ReadLine(), ct).ConfigureAwait(false);

            // Return default value if input is null, empty, or whitespace
            if (string.IsNullOrWhiteSpace(input))
            {
                return request.DefaultValue ?? string.Empty;
            }

            return input;
        }
    }

    /// <inheritdoc />
    public Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return GetInputCoreAsync();

        async Task<string> GetInputCoreAsync()
        {
            LogRequestingHumanInputForAgent(context.AgentId, context.TaskId);

            var request = HumanInputRequest.Create(
                context.Prompt,
                HumanInputType.Text,
                context.DefaultValue
            );

            return await RequestInputAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return GetConfirmationCoreAsync();

        async Task<bool> GetConfirmationCoreAsync()
        {
            LogRequestingHumanConfirmationForAgent(context.AgentId, context.TaskId);

            var request = HumanInputRequest.Create(
                $"{context.Prompt} (y/n)",
                HumanInputType.Approval,
                "n"
            );

            var response = await RequestInputAsync(request, cancellationToken).ConfigureAwait(false);
            return string.Equals(response.Trim(), "y", StringComparison.OrdinalIgnoreCase) || string.Equals(response.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return GetChoiceCoreAsync();

        async Task<string> GetChoiceCoreAsync()
        {
            LogRequestingHumanChoiceForAgent(context.AgentId, context.TaskId);

            if (context.Options == null || context.Options.Count == 0)
            {
                // If no options provided, just get regular input
                return await GetInputAsync(context, cancellationToken).ConfigureAwait(false);
            }

            var request = HumanInputRequest.Create(
                context.Prompt,
                HumanInputType.Choice,
                context.DefaultValue,
                context.Options.ToArray()
            );

            var response = await RequestInputAsync(request, cancellationToken).ConfigureAwait(false);

            // If user entered a number, convert to the actual choice
            if (int.TryParse(response, out var index) && index > 0 && index <= context.Options.Count)
            {
                return context.Options.ElementAt(index - 1);
            }

            // Otherwise return the response as-is (might be the actual choice text)
            return response;
        }
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Console is always available
        return Task.FromResult(true);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Requesting human input for agent: {AgentId}, task: {TaskId}")]
    private partial void LogRequestingHumanInputForAgent(object agentId, object taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Requesting human confirmation for agent: {AgentId}, task: {TaskId}")]
    private partial void LogRequestingHumanConfirmationForAgent(object agentId, object taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Requesting human choice for agent: {AgentId}, task: {TaskId}")]
    private partial void LogRequestingHumanChoiceForAgent(object agentId, object taskId);

}
