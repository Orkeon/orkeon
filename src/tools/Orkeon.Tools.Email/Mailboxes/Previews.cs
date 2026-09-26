using System.Text;
using Orkeon.Tools.Email.Constants;

namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>Short, single-line previews for search pages.</summary>
internal static class Previews
{
    /// <summary>The first characters of <paramref name="text"/>, whitespace collapsed; null when empty.</summary>
    public static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var builder = new StringBuilder(Math.Min(text.Length, EmailDefaults.PreviewChars + 1));
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            builder.Append(c);
            pendingSpace = false;
            if (builder.Length >= EmailDefaults.PreviewChars)
            {
                builder.Append('…');
                break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
