using System.Text.RegularExpressions;

namespace Orkeon.Hosting;

/// <summary>
/// The configuration binder's refusal of a value it cannot convert, reworded without that value (#465):
/// the value is precisely not of the key's type, so it may be anything written there by mistake, an API
/// key included, and the refusal reaches the start-up output and the logs.
/// </summary>
internal static partial class BinderRefusal
{
    /// <summary>The refusal of a value at <paramref name="path"/> that does not convert to <paramref name="type"/>, without the value.</summary>
    internal static string Unconvertible(string path, string type) =>
        $"Failed to convert the configuration value at '{path}' to type '{type}'.";

    /// <summary>
    /// <paramref name="message"/> with the value taken out when it is the binder's sentence
    /// (<c>Failed to convert configuration value '…' at '…' to type '…'.</c>); any other message as it is.
    /// </summary>
    internal static string WithoutValue(string message)
    {
        var match = Sentence().Match(message);
        return match.Success ? Unconvertible(match.Groups["path"].Value, match.Groups["type"].Value) : message;
    }

    // The value may hold quotes; a path and a type hold none, so the last " at '" ends the value.
    [GeneratedRegex(
        @"^Failed to convert configuration value '.*' at '(?<path>[^']*)' to type '(?<type>[^']*)'\.$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sentence();
}
