using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Tools.Chat;

/// <summary>Where a page resumes: the service's page and how much of it was shown.</summary>
/// <param name="Place">What the page is of.</param>
/// <param name="Top">The service's page size.</param>
/// <param name="Page">The service's paging token, or null for its first page.</param>
/// <param name="Offset">How many items of that page were shown.</param>
internal sealed record ChatCursor(string Place, int Top, string? Page, int Offset)
{
    private const int MaxLength = 8000;

    /// <summary>The cursor as a result shows it.</summary>
    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// The cursor a call passes back, or a first page. It comes from a model: only paging parameters
    /// are taken from it, and it is refused on another place than its own.
    /// </summary>
    public static ChatCursor Decode(string? text, string place, int top)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ChatCursor(place, top, null, 0);
        try
        {
            var encoded = text.Trim().Replace('-', '+').Replace('_', '/');
            if (encoded.Length > MaxLength)
                throw Invalid();
            encoded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');
            var cursor = JsonSerializer.Deserialize<ChatCursor>(Convert.FromBase64String(encoded)) ?? throw Invalid();
            if (cursor.Place != place || cursor.Top < 1 || cursor.Top > Constants.ChatDefaults.MaxPage || cursor.Offset < 0)
                throw Invalid();
            if (cursor.Page is { } page && Providers.Teams.GraphClient.PagingFragment(page) != page)
                throw Invalid();
            return cursor;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw Invalid(ex);
        }
    }

    private static ChatToolException Invalid(Exception? cause = null)
    {
        const string message = "`cursor` is not one this call returned: pass `next_cursor` of the previous page unchanged, with the same arguments.";
        return cause is null ? new ChatToolException(ChatErrorCode.InvalidRequest, message) : new ChatToolException(ChatErrorCode.InvalidRequest, message, cause);
    }
}

/// <summary>Mapping and sizing shared by the chat tools.</summary>
internal static class ChatToolHelpers
{
    /// <summary>Room kept free under the agent loop's cap, for what its rendering may add.</summary>
    private const int RenderingMargin = 200;

    private const int ZeroWidthJoiner = 0x200D;

    /// <summary>A result as the agent loop renders it: compact JSON, relaxed escaping, and no null member.</summary>
    private static readonly JsonSerializerOptions Rendering = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The characters a result of <paramref name="toolName"/> may take: what the agent loop keeps of it, less a margin.</summary>
    public static int ResultBudget(string toolName) => AgentDefaults.ResolveMaxToolResultLength(toolName) - RenderingMargin;

    /// <summary>
    /// <paramref name="text"/> on one line, as it reads: controls and line or paragraph separators are
    /// spaces, and invisible format characters (bidi overrides, zero-width ones, tags) are dropped.
    /// The joiner of emoji sequences stays.
    /// </summary>
    public static string OneLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                line.Append(' ');
            else if (category != UnicodeCategory.Format || rune.Value == ZeroWidthJoiner)
                line.Append(rune.ToString());
        }

        return line.ToString();
    }

    /// <summary>The length of <paramref name="result"/> as the agent loop renders it.</summary>
    public static int RenderedLength<T>(T result) => JsonSerializer.Serialize(result, Rendering).Length;

    /// <summary>How many of <paramref name="items"/>, one at least, fit in <paramref name="budget"/> beside <paramref name="envelope"/>.</summary>
    public static int Fit<TItem, TEnvelope>(IReadOnlyList<TItem> items, TEnvelope envelope, int budget)
    {
        ArgumentNullException.ThrowIfNull(items);
        var length = RenderedLength(envelope);
        for (var i = 0; i < items.Count; i++)
        {
            length += RenderedLength(items[i]) + 1;
            if (length > budget)
                return Math.Max(1, i);
        }

        return items.Count;
    }

    /// <summary>The page size a call asks for, within Graph's bounds.</summary>
    public static int Limit(int? requested) => Math.Clamp(requested ?? Constants.ChatDefaults.DefaultPage, 1, Constants.ChatDefaults.MaxPage);

    /// <summary>The date in <paramref name="value"/>, or null when empty; a refusal that names <paramref name="argument"/> otherwise.</summary>
    public static DateTimeOffset? ParseDate(string? value, string argument)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date;
        throw new ChatToolException(ChatErrorCode.InvalidRequest, $"`{argument}` must be a date such as 2026-10-01 or an ISO 8601 date-time.");
    }

    /// <summary>A date as results write it.</summary>
    public static string? Iso(DateTimeOffset? date) => date?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", CultureInfo.InvariantCulture);

    /// <summary>The first <paramref name="length"/> characters of <paramref name="text"/>, a surrogate pair never split.</summary>
    public static string Head(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= length)
            return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return new StringBuilder(text, 0, length, length + 1).Append('\u2026').ToString();
    }
}
