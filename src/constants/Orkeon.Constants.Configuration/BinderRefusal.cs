namespace Orkeon.Constants.Configuration;

/// <summary>
/// The configuration binder's refusal of a value it cannot convert, reworded without that value (#465):
/// the value is precisely not of the key's type, so it may be anything written there by mistake, an API
/// key included, and the refusal reaches the start-up output and the logs. One wording for the host's
/// settings check and the e-mail and drive binders (#469).
/// </summary>
internal static class BinderRefusal
{
    private const string Prefix = "Failed to convert configuration value '";
    private const string PathOpening = "' at '";
    private const string TypeOpening = "' to type '";
    private const string Ending = "'.";

    // The binder's sentence when the runtime is trimmed with UseSystemResourceKeys: the resource key, then the arguments.
    private const string ResourceKey = "Error_FailedBinding";

    /// <summary>The refusal of a value at <paramref name="path"/> that does not convert to <paramref name="type"/>, without the value.</summary>
    internal static string Unconvertible(string path, string type) =>
        $"Failed to convert the configuration value at '{path}' to type '{type}'.";

    /// <summary>
    /// <paramref name="message"/> without the value it may quote. The binder's sentence
    /// (<c>Failed to convert configuration value '…' at '…' to type '…'.</c>) becomes
    /// <see cref="Unconvertible"/>; a message that holds that sentence or its resource key but cannot be
    /// read back becomes a fixed sentence naming <paramref name="section"/> when the caller knows it;
    /// any other message, which quotes no value, is kept.
    /// </summary>
    /// <remarks>
    /// The sentence is read from its end, since the value comes first and may hold anything, quotes and
    /// <c>' at '</c> included: the type stands after the last <c>' to type '</c> and holds no quote, the
    /// path after the last <c>' at '</c> before it. A path that holds <c>' at '</c> itself is then shown
    /// by its tail only; nothing of the value can stand after that point.
    /// </remarks>
    internal static string WithoutValue(string message, string? section = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Read(message) is var (path, type))
            return Unconvertible(path, type);

        if (!MayQuoteAValue(message))
            return message;

        return section is null
            ? "Failed to convert a configuration value; the binder's message is withheld, since it quotes the value."
            : $"Failed to convert a configuration value under '{section}'; the binder's message is withheld, since it quotes the value.";
    }

    /// <summary>
    /// Whether <paramref name="exception"/> or one of its inner exceptions is the binder's refusal of a
    /// value, which it quotes, as the converter's exception inside it does.
    /// </summary>
    internal static bool QuotesAValue(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (MayQuoteAValue(current.Message))
                return true;
        }

        return false;
    }

    private static bool MayQuoteAValue(string message) =>
        message.Contains(Prefix, StringComparison.Ordinal) || message.Contains(ResourceKey, StringComparison.Ordinal);

    private static (string Path, string Type)? Read(string message)
    {
        if (!message.StartsWith(Prefix, StringComparison.Ordinal) || !message.EndsWith(Ending, StringComparison.Ordinal))
            return null;

        var typeOpening = message.LastIndexOf(TypeOpening, StringComparison.Ordinal);
        if (typeOpening < Prefix.Length)
            return null;

        var typeStart = typeOpening + TypeOpening.Length;
        var type = message[typeStart..^Ending.Length];
        if (type.Length == 0 || type.Contains('\'', StringComparison.Ordinal))
            return null;

        // The last "' at '" before the type: at or after the one that ends the value.
        // The search ends before the type's opening quote, so the two never share it.
        var pathOpening = message.LastIndexOf(PathOpening, typeOpening - 1, StringComparison.Ordinal);
        if (pathOpening < Prefix.Length)
            return null;

        var path = message[(pathOpening + PathOpening.Length)..typeOpening];
        return path.Length == 0 ? null : (path, type);
    }
}
