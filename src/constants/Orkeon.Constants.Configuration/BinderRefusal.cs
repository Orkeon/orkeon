namespace Orkeon.Constants.Configuration;

/// <summary>
/// The configuration binder's refusal of a value it cannot convert, reworded without that value (#465):
/// the value is precisely not of the key's type, so it may be anything written there by mistake, an API
/// key included, and the refusal reaches the start-up output and the logs. One wording for the host's
/// settings check, the MCP servers, <c>orkeon-host</c>'s sections and the e-mail and drive binders (#469).
/// <para>
/// The rule: a refusal built from a binding or conversion exception prints the key path the binder's
/// sentence names, or the section the caller knows, and a fixed sentence; never the message of an
/// exception that may quote the value (the binder's sentence, a converter's or a parser's).
/// </para>
/// <para>
/// Why here, in a satellite that otherwise holds constants: the host, <c>orkeon-host</c> (through the
/// host's <c>RunnerHost.Refused</c>), the e-mail and the drive families all reference this satellite and
/// no other common project short of <c>Orkeon.Domain</c>, which carries no configuration wording.
/// It reaches them across packages (the satellite ships in <c>Orkeon</c>, the host in
/// <c>Orkeon.Scripting</c>, the two families in <c>Orkeon.Tools</c>), whose nuspec dependencies are a
/// lower bound: every tag packs the whole lineup at one version, but a consumer may pair a newer
/// <c>Orkeon</c> with an older <c>Orkeon.Tools</c>. So a member is added, never changed or removed,
/// as for the OAuth layer ADR-013 shares the same way.
/// </para>
/// </summary>
internal static class BinderRefusal
{
    private const string Prefix = "Failed to convert configuration value '";
    private const string PathOpening = "' at '";
    private const string TypeOpening = "' to type '";
    private const string Ending = "'.";

    // The binder's sentence when the runtime is trimmed with UseSystemResourceKeys: the resource key, then the arguments.
    private const string ResourceKey = "Error_FailedBinding";

    // The converters' and the parsers' sentences that quote the input: TypeConverter ("x is not a valid value
    // for TimeSpan."), Enum.Parse ("Requested value 'x' was not found."), the number parsers ("The input string
    // 'x' was not in a correct format."), bool and date parsers ("String 'x' was not recognized as a valid
    // Boolean."). Read in English: under another UI culture the exception's type still tells (see QuotesAValue).
    private static readonly string[] ConverterMarkers =
    [
        " is not a valid value for ",
        "Requested value '",
        "The input string '",
        " was not in a correct format",
        " was not recognized as a valid ",
    ];

    /// <summary>The refusal of a value at <paramref name="path"/> that does not convert to <paramref name="type"/>, without the value.</summary>
    internal static string Unconvertible(string path, string type) =>
        $"Failed to convert the configuration value at '{path}' to type '{type}'.";

    /// <summary>
    /// <paramref name="message"/> without the value it may quote. The binder's sentence
    /// (<c>Failed to convert configuration value '…' at '…' to type '…'.</c>) becomes
    /// <see cref="Unconvertible"/>; a message that holds that sentence or its resource key but cannot be
    /// read back, or a converter's or a parser's sentence, becomes a fixed sentence naming
    /// <paramref name="section"/> when the caller knows it; any other message, which quotes no value, is kept.
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

        return MayQuoteAValue(message) ? Withheld(section) : message;
    }

    /// <summary>
    /// The refusal of <paramref name="exception"/> without the value: the binder's sentence read back from
    /// anywhere in the chain becomes <see cref="Unconvertible"/>; a chain that may quote a value
    /// (<see cref="QuotesAValue"/>) but holds no sentence that reads back becomes the fixed sentence of
    /// <see cref="WithoutValue(string, string?)"/>, naming <paramref name="section"/> when the caller knows it;
    /// any other chain keeps the outer message, which then holds no converter's or binder's sentence.
    /// </summary>
    internal static string WithoutValue(Exception exception, string? section = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (Read(current.Message) is var (path, type))
                return Unconvertible(path, type);
        }

        return QuotesAValue(exception) ? Withheld(section) : exception.Message;
    }

    /// <summary>
    /// Whether <paramref name="exception"/> or one of its inner exceptions may quote a configured value:
    /// the binder's refusal of a value, a converter's or a parser's message, or a conversion exception
    /// (<see cref="FormatException"/>, <see cref="UriFormatException"/> among them, <see cref="OverflowException"/>,
    /// <see cref="InvalidCastException"/>), whatever its wording.
    /// </summary>
    internal static bool QuotesAValue(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is FormatException or OverflowException or InvalidCastException || MayQuoteAValue(current.Message))
                return true;
        }

        return false;
    }

    private static string Withheld(string? section) =>
        section is null
            ? "Failed to convert a configuration value; the binder's message is withheld, since it quotes the value."
            : $"Failed to convert a configuration value under '{section}'; the binder's message is withheld, since it quotes the value.";

    private static bool MayQuoteAValue(string message) =>
        message.Contains(Prefix, StringComparison.Ordinal)
        || message.Contains(ResourceKey, StringComparison.Ordinal)
        || Array.Exists(ConverterMarkers, marker => message.Contains(marker, StringComparison.Ordinal));

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
