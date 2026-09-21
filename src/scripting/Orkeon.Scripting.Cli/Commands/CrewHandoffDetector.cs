using System.Text;
using System.Text.RegularExpressions;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// Decides whether a <c>.ork.ts</c> entry script uses the DECLARATIVE shape, by looking for
/// the <c>globalThis.crew = …</c> handoff in its source.
/// </summary>
/// <remarks>
/// <para>
/// This is a source-text sniff on purpose: evaluating the script to find out would run its
/// top level twice on the pipeline path. The cost of that choice is that the sniff sees
/// everything in the file, including prose -- and the first version matched raw source, so
/// a script whose COMMENT explained the handoff was routed as declarative and then failed
/// with "did not assign globalThis.crew". Found by writing the documentation: the example
/// that teaches the two shapes necessarily mentions the line that selects one of them.
/// </para>
/// <para>
/// So comments and string literals are removed before matching. The stripper is a small
/// scanner rather than a regex because the cases that matter -- a <c>//</c> inside a URL in
/// a string, a quote inside a comment -- are exactly the ones a regex gets wrong.
/// </para>
/// </remarks>
internal static partial class CrewHandoffDetector
{
    // Up to 60 characters between the two halves, which is what admits the TypeScript cast
    // `(globalThis as any).crew =` alongside the bare `globalThis.crew =`.
    [GeneratedRegex(@"\bglobalThis\b[^\r\n]{0,60}?\.\s*crew\s*=")]
    private static partial Regex HandoffPattern();

    /// <summary>Whether <paramref name="source"/> assigns <c>globalThis.crew</c> in code.</summary>
    public static bool DeclaresHandoff(string source)
        => !string.IsNullOrEmpty(source) && HandoffPattern().IsMatch(StripCommentsAndStrings(source));

    /// <summary>
    /// Replaces every comment and every string/template literal body with spaces, keeping
    /// newlines so the regex's "same line" constraint still means the same thing.
    /// </summary>
    internal static string StripCommentsAndStrings(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            if (c == '/' && Peek(source, i + 1) == '/')
                i = SkipLineComment(source, i, sb);
            else if (c == '/' && Peek(source, i + 1) == '*')
                i = SkipBlockComment(source, i, sb);
            else if (c is '"' or '\'' or '`')
                i = SkipLiteral(source, i, sb);
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>The character at <paramref name="index"/>, or <c>'\0'</c> past the end.</summary>
    private static char Peek(string source, int index) => index < source.Length ? source[index] : '\0';

    /// <summary>Blanks a <c>//</c> comment up to (not including) its newline; returns the index after it.</summary>
    private static int SkipLineComment(string source, int i, StringBuilder sb)
    {
        while (i < source.Length && source[i] != '\n')
        {
            sb.Append(' ');
            i++;
        }

        return i;
    }

    /// <summary>Blanks a <c>/* … */</c> comment, newlines kept; returns the index after it.</summary>
    private static int SkipBlockComment(string source, int i, StringBuilder sb)
    {
        sb.Append("  ");
        i += 2;
        while (i < source.Length && !(source[i] == '*' && Peek(source, i + 1) == '/'))
        {
            sb.Append(source[i] == '\n' ? '\n' : ' ');
            i++;
        }

        if (i < source.Length)
        {
            sb.Append("  ");
            i += 2;
        }

        return i;
    }

    /// <summary>
    /// Blanks a string or template literal, its escapes included; a template literal spans
    /// lines, which are kept so line geometry survives. Returns the index after the literal.
    /// </summary>
    private static int SkipLiteral(string source, int i, StringBuilder sb)
    {
        var quote = source[i];
        sb.Append(' ');
        i++;
        while (i < source.Length)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                sb.Append("  ");
                i += 2;
                continue;
            }

            if (source[i] == quote)
            {
                sb.Append(' ');
                return i + 1;
            }

            sb.Append(source[i] == '\n' ? '\n' : ' ');
            i++;
        }

        return i;
    }
}
