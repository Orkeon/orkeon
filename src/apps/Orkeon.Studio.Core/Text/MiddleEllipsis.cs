namespace Orkeon.Studio.Core.Text;

/// <summary>
/// The one-line form of a path too long for its column: its start and its end kept, the middle
/// replaced by an ellipsis — <c>C:\Users\…\monitoring-notes\input</c>. WPF only trims at the
/// end, which hides the folder name, the part that tells two paths apart.
/// </summary>
public static class MiddleEllipsis
{
    /// <summary>The character that stands for the cut middle.</summary>
    public const string Ellipsis = "…";

    /// <summary>
    /// The longest <c>start…end</c> form of <paramref name="text"/> whose width fits in
    /// <paramref name="width"/>, the text itself when it fits whole. The end gets the larger half
    /// of the characters kept. When even the ellipsis alone does not fit, the ellipsis.
    /// </summary>
    /// <param name="text">The full text, on one line.</param>
    /// <param name="measure">The rendered width of a string, in the caller's unit.</param>
    /// <param name="width">The width available.</param>
    public static string Fit(string text, Func<string, double> measure, double width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        if (text.Length == 0 || measure(text) <= width)
            return text;

        // Width grows with the number of characters kept: the longest that fits is found by
        // bisection, about log2(n) measures instead of n.
        int low = 0, high = text.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (measure(Keep(text, mid)) <= width)
                low = mid;
            else
                high = mid - 1;
        }

        return Keep(text, low);
    }

    /// <summary><paramref name="kept"/> characters of <paramref name="text"/> around the ellipsis.</summary>
    public static string Keep(string text, int kept)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (kept >= text.Length)
            return text;

        var head = kept / 2;
        var tail = kept - head;
        return string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(text.Length - tail));
    }
}
