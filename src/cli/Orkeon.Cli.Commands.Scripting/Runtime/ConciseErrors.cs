namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// Extracts the one human-actionable line from a (possibly deeply wrapped) failure, for
/// transcript-facing error messages. A crew failure travels as
/// <c>PromiseRejectedException(ObjectWrapper(AggregateException(HttpRequestException(SocketException))))</c>
/// and its <see cref="Exception.Message"/> embeds the FULL stringified stack — which used to be
/// dumped verbatim into the REPL transcript (the live <c>/analyze</c> incident). The full
/// exception belongs in the logs; the transcript gets the root cause:
/// <c>Resource temporarily unavailable (api.moonshot.ai:443)</c>.
/// </summary>
internal static class ConciseErrors
{
    /// <summary>Unwraps the known wrapper layers and returns the first line of the root cause's message.</summary>
    public static string Message(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var ex = exception;
        while (true)
        {
            switch (ex)
            {
                case Jint.Runtime.PromiseRejectedException rejected:
                    if (CarriedException(rejected) is { } carried) { ex = carried; continue; }
                    // Script-authored rejection (a JS string/Error): show the value, not the
                    // "Promise was rejected with value …" wrapper sentence.
                    return FirstLine(RejectedValueText(rejected) ?? rejected.Message);
                case AggregateException agg when agg.InnerExceptions.Count > 0:
                    ex = agg.Flatten().InnerExceptions[0];
                    continue;
                case System.Reflection.TargetInvocationException { InnerException: { } inner }:
                    ex = inner;
                    continue;
                default:
                    return FirstLine(ex.Message);
            }
        }
    }

    /// <summary>The CLR exception a JS rejection carries, when Jint wrapped one (faulted interop task).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Error-rendering fault barrier: converting a JS rejection value back to CLR must never throw while formatting an error message; any conversion failure falls back to the raw message.")]
    private static Exception? CarriedException(Jint.Runtime.PromiseRejectedException rejected)
    {
        try { return rejected.RejectedValue?.ToObject() as Exception; }
        catch { return null; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Error-rendering fault barrier: stringifying a JS rejection value must never throw while formatting an error message; any failure falls back to the raw message.")]
    private static string? RejectedValueText(Jint.Runtime.PromiseRejectedException rejected)
    {
        try { return rejected.RejectedValue?.ToString(); }
        catch { return null; }
    }

    /// <summary>A message that itself embeds a stringified stack keeps only its first line.</summary>
    private static string FirstLine(string message)
    {
        var newline = message.IndexOf('\n', StringComparison.Ordinal);
        return newline >= 0 ? message[..newline].TrimEnd('\r') : message;
    }
}
