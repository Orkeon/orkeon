namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The one way a text read by System One or carried in a prompt is cut to a number of characters (#434): never
/// inside a surrogate pair, so a cut text stays valid UTF-16 and an emoji is never split in two.
/// </summary>
internal static class TextCut
{
    /// <summary>
    /// The characters of <paramref name="text"/> a cut at <paramref name="max"/> keeps from its start: <paramref name="max"/>,
    /// one less when the last of them would be the first half of a surrogate pair.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The characters kept at most.</param>
    internal static int HeadLength(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (max <= 0)
            return 0;
        if (text.Length <= max)
            return text.Length;
        return char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
    }

    /// <summary><paramref name="text"/> within <paramref name="max"/> characters, its start kept.</summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The characters kept at most.</param>
    internal static string Head(string text, int max) => text[..HeadLength(text, max)];

    /// <summary>
    /// <paramref name="text"/> unchanged within <paramref name="max"/> characters; else its first <paramref name="max"/>
    /// followed by <paramref name="marker"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The characters of the text kept at most.</param>
    /// <param name="marker">What ends a text that was cut.</param>
    internal static string Head(string text, int max, string marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return text.Length <= max ? text : Head(text, max) + marker;
    }

    /// <summary>
    /// <paramref name="text"/> within <paramref name="max"/> characters, its start and its end kept around
    /// <paramref name="elision"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The characters of the result at most.</param>
    /// <param name="elision">What stands for the middle that was cut.</param>
    internal static string Middle(string text, int max, string elision)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(elision);
        if (text.Length <= max)
            return text;
        var half = Math.Max(0, (max - elision.Length) / 2);
        var head = HeadLength(text, half);
        var tailStart = text.Length - half;
        if (tailStart < text.Length && char.IsLowSurrogate(text[tailStart]))
            tailStart++;
        return string.Concat(text.AsSpan(0, head), elision, text.AsSpan(tailStart));
    }
}
