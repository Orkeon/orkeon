using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian that checks output content by delegating to the IOutputValidationPipeline.
/// </summary>
public partial class OutputGuard : IGuardian
{
    private readonly IOutputValidationPipeline _validationPipeline;
    private readonly ILogger<OutputGuard> _logger;

    /// <summary>Initializes a new instance of <see cref="OutputGuard"/>.</summary>
    /// <param name="validationPipeline">The output validation pipeline used to validate output content.</param>
    /// <param name="logger">The logger.</param>
    public OutputGuard(IOutputValidationPipeline validationPipeline, ILogger<OutputGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(validationPipeline);
        _validationPipeline = validationPipeline;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckCoreAsync();

        async Task<GuardResult> CheckCoreAsync()
        {
            if (context.Phase != GuardPhase.Output || context.Content is null)
                return GuardResult.Allow();

            var validationContext = new OutputValidationContext(
                ExpectedFormat: OutputFormat.Text);

            var result = await _validationPipeline.ValidateAsync(context.Content, validationContext, ct).ConfigureAwait(false);

            if (!result.IsValid)
            {
                var violations = result.Results
                    .Where(r => !r.IsValid)
                    .Select(r => new GuardViolation(
                        nameof(OutputGuard),
                        GuardPhase.Output,
                        r.ErrorMessage ?? "Output validation failed",
                        GuardThreatSeverity.Medium,
                        DateTime.UtcNow))
                    .ToList();

                LogOutputBlockedByValidationPipeline(result.CombinedErrorMessage ?? "Output validation failed");
                return GuardResult.Block(
                    result.CombinedErrorMessage ?? "Output validation failed", violations);
            }

            return GuardResult.Allow();
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Output blocked by validation pipeline: {ErrorMessage}")]
    private partial void LogOutputBlockedByValidationPipeline(string errorMessage);

}
