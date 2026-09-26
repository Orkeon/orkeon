using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Mailboxes.Graph;

/// <summary>
/// Translates a search into a Graph query. Graph offers two paths that do not combine:
/// <c>$filter</c> + <c>$orderby</c> for marks and dates, <c>$search</c> (KQL) for text. A search
/// with text takes the second path and applies the marks and dates to the page client-side, so
/// such a page can hold fewer messages than asked for.
/// </summary>
internal static class GraphQueries
{
    private const string EpochBound = "1900-01-01T00:00:00Z";

    /// <summary>Whether <paramref name="search"/> needs <c>$search</c>.</summary>
    public static bool UsesSearch(MailSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return !string.IsNullOrWhiteSpace(search.From) || !string.IsNullOrWhiteSpace(search.To)
            || !string.IsNullOrWhiteSpace(search.Subject) || !string.IsNullOrWhiteSpace(search.Text)
            || !string.IsNullOrWhiteSpace(search.RawQuery);
    }

    /// <summary>The relative address listing a folder's messages for <paramref name="search"/>.</summary>
    public static string MessagesQuery(string folderId, MailSearch search, string select)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = new StringBuilder()
            .Append("me/mailFolders/").Append(Uri.EscapeDataString(folderId)).Append("/messages")
            .Append("?$select=").Append(select)
            .Append("&$top=").Append(search.Limit.ToString(CultureInfo.InvariantCulture));

        if (UsesSearch(search))
        {
            query.Append("&$search=").Append(Uri.EscapeDataString($"\"{Kql(search)}\""));
            return query.ToString();
        }

        query.Append("&$orderby=").Append(Uri.EscapeDataString("receivedDateTime desc"));
        var filter = Filter(search);
        if (filter.Length > 0)
            query.Append("&$filter=").Append(Uri.EscapeDataString(filter));
        return query.ToString();
    }

    /// <summary>
    /// The <c>$filter</c> of a mark-and-date search. When anything is filtered, the ordering
    /// property must be filtered first, which Graph otherwise answers with InefficientFilter.
    /// </summary>
    public static string Filter(MailSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var clauses = new List<string>();
        if (search.UnreadOnly)
            clauses.Add("isRead eq false");
        if (search.FlaggedOnly)
            clauses.Add("flag/flagStatus eq 'flagged'");
        if (search.HasAttachments is { } attachments)
            clauses.Add(attachments ? "hasAttachments eq true" : "hasAttachments eq false");
        if (search.Before is { } before)
            clauses.Add($"receivedDateTime lt {Iso(before)}");

        if (clauses.Count == 0 && search.Since is null)
            return string.Empty;

        clauses.Insert(0, $"receivedDateTime ge {(search.Since is { } since ? Iso(since) : EpochBound)}");
        return string.Join(" and ", clauses);
    }

    /// <summary>The KQL of a text search; each word of a property criterion must match.</summary>
    public static string Kql(MailSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var terms = new List<string>();
        AddProperty(terms, "from", search.From);
        AddProperty(terms, "to", search.To);
        AddProperty(terms, "subject", search.Subject);
        terms.AddRange(Words(search.Text));
        if (!string.IsNullOrWhiteSpace(search.RawQuery))
            terms.Add(Clean(search.RawQuery));
        return string.Join(' ', terms);
    }

    /// <summary>Applies to a <c>$search</c> page the criteria KQL did not carry.</summary>
    public static bool PostFilter(MailSearch search, MessageSummaryInfo summary)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(summary);
        if (search.UnreadOnly && summary.Seen != false)
            return false;
        if (search.FlaggedOnly && summary.Flagged != true)
            return false;
        if (search.HasAttachments is { } attachments && summary.HasAttachments != attachments)
            return false;
        if (search.Since is { } since && (summary.Date is not { } afterDate || afterDate < since))
            return false;
        return search.Before is not { } before || (summary.Date is { } beforeDate && beforeDate < before);
    }

    private static void AddProperty(List<string> terms, string property, string? value)
    {
        foreach (var word in Words(value))
            terms.Add($"{property}:{word}");
    }

    private static string[] Words(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : Clean(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Removes the characters that would break out of the quoted <c>$search</c> value.</summary>
    private static string Clean(string value) =>
        value.Replace("\"", string.Empty, StringComparison.Ordinal).Replace("\\", string.Empty, StringComparison.Ordinal).Trim();

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
