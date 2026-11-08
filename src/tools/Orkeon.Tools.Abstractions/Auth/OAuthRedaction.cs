namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>Keeps credentials out of a message: an identity provider may echo what it was sent.</summary>
internal static class OAuthRedaction
{
    /// <summary>The shortest credential that is blanked: a one-letter value would blank the sentence, and real ones are longer.</summary>
    private const int MinimumLength = 4;

    /// <summary><paramref name="message"/> with every credential of <paramref name="credentials"/> replaced by <c>***</c>.</summary>
    public static string Redact(string message, params string?[] credentials)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(credentials);
        foreach (var credential in credentials)
        {
            if (credential is { Length: >= MinimumLength })
                message = message.Replace(credential, "***", StringComparison.Ordinal);
        }

        return message;
    }
}
