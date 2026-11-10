using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew;

/// <summary>
/// The fragments of the tool calls one streamed turn builds, held by index until the turn's stream ends, so a DLP
/// interceptor scans the arguments of each call whole (a secret cut between two fragments escapes a scan of each
/// piece). Not thread-safe: one turn's enumeration owns it.
/// </summary>
public sealed class ToolCallDeltaHold
{
    private static readonly Action<ILogger, Exception?> DlpFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, nameof(DlpFailed)),
        "The DLP interceptor failed on the arguments of a streamed tool call; they are not streamed.");

    private readonly SortedDictionary<int, Call> _calls = new();

    /// <summary>Keeps one fragment with the call it belongs to.</summary>
    /// <param name="fragment">The fragment.</param>
    public void Add(ToolCallDelta fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_calls.TryGetValue(fragment.Index, out var call))
            _calls[fragment.Index] = call = new Call(fragment.Index);
        call.Id ??= fragment.Id;
        call.Name ??= fragment.Name;
        call.Arguments.Append(fragment.ArgumentsDelta);
    }

    /// <summary>
    /// Each held call as one delta, in index order, whose arguments went through the interceptor. An interceptor that
    /// fails leaves the call with empty arguments (the failure is closed, never open).
    /// </summary>
    /// <param name="dlp">The interceptor and its policy.</param>
    /// <param name="logger">Where an interceptor failure is reported; null reports nothing.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One delta per call.</returns>
    public async System.Threading.Tasks.Task<IReadOnlyList<ToolCallDelta>> RedactAsync(
        ToolCallDeltaDlp dlp, ILogger? logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dlp);
        var redacted = new List<ToolCallDelta>(_calls.Count);
        foreach (var call in _calls.Values)
        {
            var arguments = call.Arguments.ToString();
            if (arguments.Length > 0)
            {
                try
                {
                    var result = await dlp.Interceptor.InterceptAsync(arguments, dlp.Policy, cancellationToken).ConfigureAwait(false);
                    arguments = result.ProcessedContent ?? string.Empty;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (logger is not null)
                        DlpFailed(logger, ex);
                    arguments = string.Empty;
                }
            }

            redacted.Add(new ToolCallDelta(call.Index, call.Id, call.Name, arguments));
        }

        return redacted;
    }

    private sealed class Call
    {
        public Call(int index)
        {
            Index = index;
            Arguments = new StringBuilder();
        }

        public int Index { get; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; }
    }
}
