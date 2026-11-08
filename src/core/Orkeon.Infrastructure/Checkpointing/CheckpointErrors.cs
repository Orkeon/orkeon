using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>What a run's failure may carry into the state store: its message, with the secrets it quotes masked.</summary>
internal static class CheckpointErrors
{
    /// <summary>An exception whose message is <paramref name="ex"/>'s, passed through the log sanitizer (the store keeps it in plaintext).</summary>
    public static Exception Redacted(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return new InvalidOperationException(LogSanitizer.SanitizeString(ex.Message));
    }
}
