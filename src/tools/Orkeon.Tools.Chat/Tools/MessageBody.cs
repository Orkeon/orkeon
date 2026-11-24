using System.Globalization;
using System.Text;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>The text and the mentions a call writes, for a post and for a rewrite alike.</summary>
internal static class MessageBody
{
    /// <summary>What a wrong <c>format</c> is answered.</summary>
    public const string FormatRefusal = "`format` must be markdown or text";

    private const string Markdown = "markdown";
    private const string PlainText = "text";

    private static readonly string[] FormatNames = [Markdown, PlainText];

    /// <summary>Whether <paramref name="format"/> is one, or empty.</summary>
    public static bool IsFormat(string? format) => Format(format) is not null;

    /// <summary>Whether <paramref name="format"/> is Markdown, the default.</summary>
    public static bool IsMarkdown(string? format) => Format(format) == Markdown;

    /// <summary>The refusal of <paramref name="ids"/>, or null.</summary>
    public static string? RefuseMentions(IReadOnlyList<string>? ids)
    {
        if (ids is { Count: > ChatDefaults.MaxMentions })
            return string.Create(CultureInfo.InvariantCulture, $"`mentions` holds at most {ChatDefaults.MaxMentions} ids");
        return ids?.Any(id => id is null || !ChatIds.IsDirectoryId(id)) == true
            ? "`mentions` holds ids of people from chat_members, not names"
            : null;
    }

    /// <summary>The HTML of <paramref name="text"/>, within the size a message may have.</summary>
    public static string Render(string text, string? format)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Bounded ahead of the rendering too: every character is a byte of HTML at least.
        var html = text.Length > ChatDefaults.MaxMessageBytes
            ? text
            : Format(format) == PlainText ? MarkdownRenderer.FromText(text) : MarkdownRenderer.FromMarkdown(text);
        if (html.Length == 0)
            throw new ChatToolException(ChatErrorCode.InvalidRequest, "`text` renders to an empty message; nothing was posted.");
        if (Encoding.UTF8.GetByteCount(html) <= ChatDefaults.MaxMessageBytes)
            return html;
        throw new ChatToolException(
            ChatErrorCode.InvalidRequest,
            string.Create(CultureInfo.InvariantCulture, $"The message is over {ChatDefaults.MaxMessageBytes / 1024} KB once rendered; nothing was posted. Shorten it, or split it."));
    }

    /// <summary>The people of <paramref name="ids"/>, from the directory.</summary>
    public static async Task<IReadOnlyList<ChatPerson>> MentionsAsync(IChatProvider provider, IReadOnlyList<string>? ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var people = new List<ChatPerson>();
        foreach (var id in (ids ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            people.Add(await provider.GetPersonAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new ChatToolException(ChatErrorCode.NotFound, $"`mentions` holds '{id}', who is not in the directory; nothing was posted."));
        }

        return people;
    }

    private static string? Format(string? format) =>
        string.IsNullOrWhiteSpace(format) ? Markdown : FormatNames.FirstOrDefault(known => string.Equals(known, format.Trim(), StringComparison.OrdinalIgnoreCase));
}
