using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Graph;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Graph;

/// <summary>The two Graph query paths that do not combine: <c>$filter</c>/<c>$orderby</c> and <c>$search</c>.</summary>
public sealed class GraphQueriesTests
{
    private static readonly DateTimeOffset September = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("subject")]
    [InlineData("text")]
    [InlineData("raw")]
    public void Should_take_the_search_path_for_any_text_criterion(string criterion)
    {
        var search = criterion switch
        {
            "from" => new MailSearch { From = "a" },
            "to" => new MailSearch { To = "a" },
            "subject" => new MailSearch { Subject = "a" },
            "text" => new MailSearch { Text = "a" },
            _ => new MailSearch { RawQuery = "a" },
        };

        Assert.True(GraphQueries.UsesSearch(search));
    }

    [Fact]
    public void Should_take_the_filter_path_for_marks_dates_and_attachments()
    {
        Assert.False(GraphQueries.UsesSearch(new MailSearch { UnreadOnly = true, FlaggedOnly = true, HasAttachments = false, Since = September, From = "  " }));
    }

    [Fact]
    public void Should_filter_nothing_When_there_is_no_criterion()
    {
        Assert.Equal(string.Empty, GraphQueries.Filter(new MailSearch()));
    }

    [Fact]
    public void Should_put_the_ordering_property_first_and_convert_dates_to_UTC()
    {
        var filter = GraphQueries.Filter(new MailSearch
        {
            HasAttachments = false,
            Since = new DateTimeOffset(2026, 9, 1, 9, 30, 15, TimeSpan.FromHours(-4)),
        });

        Assert.Equal("receivedDateTime ge 2026-09-01T13:30:15Z and hasAttachments eq false", filter);
    }

    [Fact]
    public void Should_bound_a_before_only_filter_from_the_epoch()
    {
        Assert.Equal(
            "receivedDateTime ge 1900-01-01T00:00:00Z and receivedDateTime lt 2026-09-01T00:00:00Z",
            GraphQueries.Filter(new MailSearch { Before = September }));
    }

    [Fact]
    public void Should_build_KQL_with_one_property_term_per_word_and_strip_quote_escapes()
    {
        var kql = GraphQueries.Kql(new MailSearch
        {
            From = "Alice Martin",
            To = "team",
            Subject = "  \"Q3\\ budget\"  ",
            Text = "forecast  draft",
            RawQuery = "received>=2026-09-01 \"x\"",
        });

        Assert.Equal("from:Alice from:Martin to:team subject:Q3 subject:budget forecast draft received>=2026-09-01 x", kql);
    }

    [Fact]
    public void Should_escape_the_folder_id_and_quote_the_search()
    {
        var query = GraphQueries.MessagesQuery("AAMk/+=", new MailSearch { Text = "invoice", Limit = 7 }, "id,subject");

        Assert.Equal("me/mailFolders/AAMk%2F%2B%3D/messages?$select=id,subject&$top=7&$search=%22invoice%22", query);
    }

    [Theory]
    [InlineData(false, null, null, "2026-09-10", true)]
    [InlineData(true, null, null, "2026-09-10", false)]
    [InlineData(null, null, null, null, false)]
    public void Should_post_filter_marks_and_dates_that_KQL_did_not_carry(bool? seen, bool? flagged, bool? attachments, string? date, bool kept)
    {
        var search = new MailSearch { UnreadOnly = true, Since = September, Before = September.AddMonths(1) };
        var summary = new MessageSummaryInfo
        {
            Id = "graph:x",
            From = "a@example.com",
            Subject = "s",
            Seen = seen,
            Flagged = flagged,
            HasAttachments = attachments,
            Date = date is null ? null : DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(kept, GraphQueries.PostFilter(search, summary));
    }

    [Fact]
    public void Should_post_filter_flags_and_attachments()
    {
        var flaggedWithFiles = new MailSearch { FlaggedOnly = true, HasAttachments = true };
        var summary = new MessageSummaryInfo { Id = "graph:x", From = "a", Subject = "s", Flagged = true, HasAttachments = true };

        Assert.True(GraphQueries.PostFilter(flaggedWithFiles, summary));
        Assert.False(GraphQueries.PostFilter(flaggedWithFiles, summary with { Flagged = false }));
        Assert.False(GraphQueries.PostFilter(flaggedWithFiles, summary with { HasAttachments = false }));
        Assert.True(GraphQueries.PostFilter(new MailSearch { Before = September }, summary with { Date = September.AddDays(-1) }));
        Assert.False(GraphQueries.PostFilter(new MailSearch { Before = September }, summary with { Date = September }));
    }
}
