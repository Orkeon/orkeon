using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive;

/// <summary>Mapping and sizing shared by the drive tools.</summary>
internal static class DriveToolHelpers
{
    /// <summary>Room kept free under the agent loop's cap, for what its rendering may add.</summary>
    private const int RenderingMargin = 200;

    /// <summary>The least text a read returns, whatever the metadata takes, so that reading always advances.</summary>
    private const int MinTextWidth = 300;

    /// <summary>
    /// A result as the agent loop renders it: compact JSON, relaxed escaping, and no null member
    /// (the tool pipeline drops them). Measured with it, a page is cut where the tool decides,
    /// with a cursor that resumes exactly there, instead of where the loop truncates it.
    /// </summary>
    private static readonly JsonSerializerOptions Rendering = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The characters a result of <paramref name="toolName"/> may take: what the agent loop keeps of it, less a margin.</summary>
    public static int ResultBudget(string toolName) => AgentDefaults.ResolveMaxToolResultLength(toolName) - RenderingMargin;

    /// <summary>The length of <paramref name="result"/> as the agent loop renders it.</summary>
    public static int RenderedLength<T>(T result) => JsonSerializer.Serialize(result, Rendering).Length;

    /// <summary>The kind of <paramref name="item"/> as the tool view names it.</summary>
    private static string TypeOf(DriveItem item)
    {
        if (item.IsFolder)
            return "folder";
        return item.IsGoogleDocument ? "google-doc" : "file";
    }

    /// <summary>The tool view of <paramref name="item"/>; listings leave its browser address out.</summary>
    public static DriveItemDto ToDto(DriveItem item, bool withUrl = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DriveItemDto
        {
            Id = item.Id,
            Name = item.Name,
            Type = TypeOf(item),
            MimeType = item.IsFolder ? null : item.MimeType,
            Size = item.Size,
            Modified = item.Modified?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ParentId = item.ParentId,
            WebUrl = withUrl ? item.WebUrl : null,
        };
    }

    /// <summary>The date in <paramref name="value"/>, or null when empty; a refusal that names <paramref name="argument"/> otherwise.</summary>
    public static DateTimeOffset? ParseDate(string? value, string argument)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date;
        throw new DriveToolException(DriveErrorCode.InvalidRequest, $"`{argument}` must be a date such as 2026-10-01 or an ISO 8601 date-time.");
    }

    /// <summary>Null when <paramref name="name"/> is a usable item name; else why not, naming <paramref name="argument"/>.</summary>
    public static string? CheckName(string? name, string argument)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"`{argument}` is required";
        var trimmed = name.Trim();
        if (trimmed is "." or ".." || trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Any(char.IsControl))
            return $"`{argument}` is a single name, without '/', '\\' or control characters";
        return trimmed.Length > 255 ? $"`{argument}` holds 255 characters at most" : null;
    }

    /// <summary>The page size a call asks for, within the family's bounds.</summary>
    public static int Limit(int? requested) =>
        Math.Clamp(requested ?? Constants.DriveDefaults.DefaultListLimit, 1, Constants.DriveDefaults.MaxListLimit);

    /// <summary>
    /// Fetches a page that fits a tool result: when the page <paramref name="fetch"/> returns
    /// renders longer than <paramref name="budget"/>, the same page is asked again, from the same
    /// cursor, with fewer items — so that its <c>next_cursor</c> resumes exactly after the last
    /// item shown, which cutting the page here could not promise.
    /// </summary>
    public static async Task<(DrivePage Page, TResponse Response)> FitPageAsync<TResponse>(
        int limit, Func<int, Task<DrivePage>> fetch, Func<DrivePage, TResponse> respond, int budget)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(respond);
        while (true)
        {
            var page = await fetch(limit).ConfigureAwait(false);
            var response = respond(page);
            var length = RenderedLength(response);
            if (length <= budget || page.Items.Count <= 1 || limit <= 1)
                return (page, response);

            // Scale the page to the room there is, and always shrink it by one at least.
            var scaled = (int)((long)page.Items.Count * budget / length);
            limit = Math.Max(1, Math.Min(scaled, page.Items.Count - 1));
        }
    }

    /// <summary>
    /// The room the text of a read has once everything else of <paramref name="emptyText"/> (the
    /// response with no text yet) is rendered.
    /// </summary>
    public static int TextWidth<T>(T emptyText, int budget) => Math.Max(MinTextWidth, budget - RenderedLength(emptyText));

    /// <summary>
    /// The longest slice of <paramref name="text"/> from <paramref name="start"/>, at most
    /// <paramref name="maxLength"/> characters, whose rendering (quotes and line breaks escaped)
    /// takes at most <paramref name="width"/> characters. A surrogate pair is never split.
    /// </summary>
    public static string Slice(string text, int start, int maxLength, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        var length = Math.Min(maxLength, text.Length - start);
        while (length > 0)
        {
            if (char.IsHighSurrogate(text[start + length - 1]))
                length--;
            var candidate = text.Substring(start, length);
            var rendered = JsonSerializer.Serialize(candidate, Rendering).Length - 2;
            if (rendered <= width)
                return candidate;
            length -= Math.Max(1, rendered - width);
        }

        return string.Empty;
    }
}
