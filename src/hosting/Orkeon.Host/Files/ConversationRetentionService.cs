using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Host.Api;

namespace Orkeon.Host.Files;

/// <summary>Deletes the conversations nobody used for a while: at start, then every hour.</summary>
internal sealed partial class ConversationRetentionService : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly ConversationFiles _files;
    private readonly TimeProvider _time;
    private readonly ILogger<ConversationRetentionService> _logger;

    public ConversationRetentionService(
        ConversationFiles files, HostApiOptionalServices optional, ILogger<ConversationRetentionService> logger)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        ArgumentNullException.ThrowIfNull(optional);
        _time = optional.Time;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            do
            {
                try
                {
                    await _files.SweepAsync(stoppingToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A pass that failed is the log's matter; the next one tries again.
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
                {
                    LogPassFailed(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host stops.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The conversations' retention pass failed")]
    private partial void LogPassFailed(Exception ex);
}
