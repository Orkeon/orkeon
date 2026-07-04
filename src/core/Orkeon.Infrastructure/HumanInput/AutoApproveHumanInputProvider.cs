using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>
/// Non-interactive <see cref="IHumanInputProvider"/> that auto-approves every request.
/// Used in batch runners and tests where no human is present.
/// </summary>
/// <remarks>
/// Behaviour:
/// - <see cref="GetInputAsync"/> returns the context's default value, or <c>"approved"</c>.
/// - <see cref="GetConfirmationAsync"/> always returns <c>true</c>.
/// - <see cref="GetChoiceAsync"/> returns the default value, the first option, or <c>"approved"</c>.
/// </remarks>
public sealed partial class AutoApproveHumanInputProvider : IHumanInputProvider
{
    private const string DefaultResponse = "approved";

    private readonly ILogger<AutoApproveHumanInputProvider> _logger;

    /// <summary>Initializes a new instance of <see cref="AutoApproveHumanInputProvider"/>.</summary>
    /// <param name="logger">Optional logger.</param>
    public AutoApproveHumanInputProvider(ILogger<AutoApproveHumanInputProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<AutoApproveHumanInputProvider>.Instance;
    }

    /// <inheritdoc />
    public Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = !string.IsNullOrEmpty(context.DefaultValue) ? context.DefaultValue : DefaultResponse;
        LogAutoApproved(context.InputType, response);
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        LogAutoApproved(context.InputType, "true");
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        string response;
        if (!string.IsNullOrEmpty(context.DefaultValue))
        {
            response = context.DefaultValue;
        }
        else
        {
            response = context.Options is { Count: > 0 } options ? options[0] : DefaultResponse;
        }
        LogAutoApproved(context.InputType, response);
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Auto-approved human input request (input_type={InputType}, response={Response})")]
    private partial void LogAutoApproved(string inputType, string response);
}
