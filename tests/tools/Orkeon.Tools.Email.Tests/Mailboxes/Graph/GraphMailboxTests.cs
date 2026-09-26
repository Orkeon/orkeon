using System.Net;
using System.Text;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Graph;

/// <summary>The Microsoft Graph mailbox against a route-based HTTP double standing for Graph.</summary>
public sealed class GraphMailboxTests
{
    private const string Base = GraphFixture.Base;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_send_the_bearer_token_and_ask_for_immutable_ids_on_every_request()
    {
        using var graph = new GraphFixture().WithFolders();

        await graph.Mailbox.ListFoldersAsync(Token);

        Assert.NotEmpty(graph.Http.Requests);
        Assert.All(graph.Http.Requests, request =>
        {
            Assert.Equal($"Bearer {GraphFixture.AccessToken}", request.Authorization);
            Assert.Equal("IdType=\"ImmutableId\"", request.Prefer);
            Assert.StartsWith(Base, request.Uri.AbsoluteUri, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Should_list_folders_with_their_well_known_roles_paths_and_counts()
    {
        using var graph = new GraphFixture().WithFolders();

        var folders = await graph.Mailbox.ListFoldersAsync(Token);

        Assert.Equal(
            ["Inbox", "Clients", "Clients/ACME", "Deleted Items", "Drafts", "Inbox/Newsletters", "Junk Email", "Sent Items"],
            folders.Select(f => f.Path));
        Assert.Equal(new MailFolderInfo("Inbox", "Inbox", FolderRoles.Inbox, 5, 2), folders[0]);
        Assert.Equal(FolderRoles.Sent, folders.Single(f => f.Path == "Sent Items").Role);
        Assert.Equal(FolderRoles.Drafts, folders.Single(f => f.Path == "Drafts").Role);
        Assert.Equal(FolderRoles.Trash, folders.Single(f => f.Path == "Deleted Items").Role);
        Assert.Equal(FolderRoles.Junk, folders.Single(f => f.Path == "Junk Email").Role);
        Assert.Equal(new MailFolderInfo("Inbox/Newsletters", "Newsletters", null, 40, 12), folders.Single(f => f.Name == "Newsletters"));
        Assert.Null(folders.Single(f => f.Path == "Clients/ACME").Role);
    }

    [Fact]
    public async Task Should_follow_folder_paging_links_only_while_they_point_at_Graph()
    {
        using var graph = new GraphFixture();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders?$top=250", FakeHttpResponse.Json($$"""
            {"value":[{{GraphFixture.Folder("id-a", "Alpha", 0, 1, 0)}}],"@odata.nextLink":"{{Base}}me/mailFolders?$skip=1&page=2"}
            """));
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders?$skip=1&page=2", FakeHttpResponse.Json($$"""
            {"value":[{{GraphFixture.Folder("id-b", "Beta", 0, 1, 0)}}],"@odata.nextLink":"https://evil.example/steal?page=3"}
            """));

        var folders = await graph.Mailbox.ListFoldersAsync(Token);

        Assert.Equal(["Alpha", "Beta"], folders.Select(f => f.Path));
        Assert.DoesNotContain(graph.Http.Requests, request => request.Uri.Host != "graph.microsoft.com");
    }

    [Fact]
    public async Task Should_filter_marks_and_dates_with_the_ordering_property_first()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json("""{"value":[]}"""));

        await graph.Mailbox.SearchAsync(new MailSearch
        {
            UnreadOnly = true,
            FlaggedOnly = true,
            HasAttachments = true,
            Since = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.FromHours(2)),
            Before = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        }, Token);

        var query = QueryOf(Assert.Single(graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?")));
        Assert.Equal(
            "receivedDateTime ge 2026-09-01T00:00:00Z and isRead eq false and flag/flagStatus eq 'flagged' and hasAttachments eq true and receivedDateTime lt 2026-09-30T00:00:00Z",
            query["$filter"]);
        Assert.Equal("receivedDateTime desc", query["$orderby"]);
        Assert.Equal("10", query["$top"]);
        Assert.Equal("id,subject,from,receivedDateTime,isRead,flag,hasAttachments,bodyPreview", query["$select"]);
        Assert.False(query.ContainsKey("$search"));
    }

    [Fact]
    public async Task Should_bound_the_ordering_property_from_the_epoch_When_only_marks_are_filtered()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json("""{"value":[]}"""));

        await graph.Mailbox.SearchAsync(new MailSearch { UnreadOnly = true, Limit = 25 }, Token);

        var query = QueryOf(Assert.Single(graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?")));
        Assert.Equal("receivedDateTime ge 1900-01-01T00:00:00Z and isRead eq false", query["$filter"]);
        Assert.Equal("25", query["$top"]);
    }

    [Fact]
    public async Task Should_order_without_filtering_When_the_search_has_no_criteria()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json("""{"value":[]}"""));

        await graph.Mailbox.SearchAsync(new MailSearch(), Token);

        var query = QueryOf(Assert.Single(graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?")));
        Assert.Equal("receivedDateTime desc", query["$orderby"]);
        Assert.False(query.ContainsKey("$filter"));
    }

    [Fact]
    public async Task Should_search_text_with_KQL_and_apply_marks_and_dates_to_the_page_client_side()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json($$"""
            {"value":[
              {{GraphFixture.Summary("m-unread", "Budget Q3", received: "2026-09-20T08:00:00Z")}},
              {{GraphFixture.Summary("m-read", "Budget Q3", isRead: true)}},
              {{GraphFixture.Summary("m-old", "Budget Q3", received: "2026-08-01T08:00:00Z")}}
            ]}
            """));

        var page = await graph.Mailbox.SearchAsync(new MailSearch
        {
            From = "alice",
            Subject = "budget \"Q3\"",
            Text = "forecast",
            RawQuery = "hasAttachments:true",
            UnreadOnly = true,
            Since = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        }, Token);

        var query = QueryOf(Assert.Single(graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?")));
        Assert.Equal("\"from:alice subject:budget subject:Q3 forecast hasAttachments:true\"", query["$search"]);
        Assert.False(query.ContainsKey("$orderby"));
        Assert.False(query.ContainsKey("$filter"));
        Assert.Equal(["graph:m-unread"], page.Messages.Select(m => m.Id));
    }

    [Fact]
    public async Task Should_summarize_each_message_of_the_page()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json($$"""
            {"value":[
              {{GraphFixture.Summary("AAMkAD=", "Report", isRead: true, flagged: true, attachments: true, preview: "Please   find\\r\\nthe report")}},
              {{GraphFixture.Summary("AAMkAE=", "Plain", name: "bob@example.com", address: "bob@example.com")}}
            ]}
            """));

        var page = await graph.Mailbox.SearchAsync(new MailSearch(), Token);

        var first = page.Messages[0];
        Assert.Equal(("graph:AAMkAD=", "Alice Martin <alice@example.com>", "Report"), (first.Id, first.From, first.Subject));
        Assert.Equal((true, true, true), (first.Seen, first.Flagged, first.HasAttachments));
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero), first.Date);
        Assert.Equal("Please find the report", first.Preview);
        Assert.Equal("bob@example.com", page.Messages[1].From);
        Assert.Equal((false, false), (page.Messages[1].Seen, page.Messages[1].Flagged));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Should_page_with_a_cursor_that_follows_the_Graph_next_link_verbatim()
    {
        using var graph = new GraphFixture().WithFolders();
        const string next = Base + "me/mailFolders/id-inbox/messages?$skiptoken=abc%3D%3D";
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?$select", FakeHttpResponse.Json($$"""
            {"value":[{{GraphFixture.Summary("p1", "First page")}}],"@odata.nextLink":"{{next}}"}
            """));
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?$skiptoken", FakeHttpResponse.Json($$"""
            {"value":[{{GraphFixture.Summary("p2", "Second page")}}]}
            """));

        var first = await graph.Mailbox.SearchAsync(new MailSearch { Limit = 1 }, Token);
        var second = await graph.Mailbox.SearchAsync(new MailSearch { Limit = 1, Cursor = first.NextCursor }, Token);

        Assert.NotNull(first.NextCursor);
        Assert.StartsWith("n:", first.NextCursor, StringComparison.Ordinal);
        Assert.Equal("graph:p2", Assert.Single(second.Messages).Id);
        Assert.Equal(next, graph.Http.Requests[^1].Uri.OriginalString);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Should_resume_inside_a_server_page_from_a_message_s_own_cursor()
    {
        using var graph = new GraphFixture().WithFolders();
        const string next = Base + "me/mailFolders/id-inbox/messages?$skiptoken=abc";
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?$select", FakeHttpResponse.Json($$"""
            {"value":[{{GraphFixture.Summary("p1", "One")}},{{GraphFixture.Summary("p2", "Two")}},{{GraphFixture.Summary("p3", "Three")}}],"@odata.nextLink":"{{next}}"}
            """));

        var page = await graph.Mailbox.SearchAsync(new MailSearch { Limit = 3 }, Token);
        var resumed = await graph.Mailbox.SearchAsync(new MailSearch { Limit = 3, Cursor = page.Messages[0].ResumeCursor }, Token);

        Assert.EndsWith(".1", page.Messages[0].ResumeCursor, StringComparison.Ordinal);
        Assert.Equal(page.NextCursor, page.Messages[2].ResumeCursor);
        Assert.Equal(["graph:p2", "graph:p3"], resumed.Messages.Select(m => m.Id));
        Assert.Equal(page.NextCursor, resumed.NextCursor);
        Assert.Equal(graph.Http.Requests[^2].Uri.OriginalString, graph.Http.Requests[^1].Uri.OriginalString);
    }

    [Theory]
    [InlineData(".0")]
    [InlineData(".-1")]
    [InlineData(".x")]
    public async Task Should_refuse_a_cursor_whose_position_it_did_not_write(string position)
    {
        using var graph = new GraphFixture();
        var cursor = "n:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Base + "me/messages")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + position;

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Mailbox.SearchAsync(new MailSearch { Cursor = cursor }, Token));

        Assert.StartsWith("`cursor` is not a cursor of this account", error.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Http.Requests);
    }

    [Fact]
    public async Task Should_hand_out_no_cursor_for_a_next_link_outside_Graph()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json($$"""
            {"value":[{{GraphFixture.Summary("p1", "First page")}}],"@odata.nextLink":"https://evil.example/collect?page=2"}
            """));

        var page = await graph.Mailbox.SearchAsync(new MailSearch { Limit = 1 }, Token);

        Assert.Single(page.Messages);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData("https://evil.example/collect?page=2")]
    [InlineData("http://graph.microsoft.com/v1.0/me/messages")]
    [InlineData("not a link")]
    public async Task Should_refuse_a_cursor_that_does_not_lead_to_Graph_without_sending_anything(string link)
    {
        using var graph = new GraphFixture().WithFolders();
        var cursor = "n:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(link)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Mailbox.SearchAsync(new MailSearch { Cursor = cursor }, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.StartsWith("`cursor` is not a cursor of this account", error.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Http.Requests);
    }

    [Theory]
    [InlineData("inbox", "id-inbox")]
    [InlineData("SENT", "id-sent")]
    [InlineData("clients/acme", "id-acme")]
    [InlineData("/Inbox/Newsletters/", "id-news")]
    public async Task Should_resolve_a_folder_by_role_or_by_path_whatever_the_case(string folder, string id)
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/{id}/messages?", FakeHttpResponse.Json("""{"value":[]}"""));

        await graph.Mailbox.SearchAsync(new MailSearch { Folder = folder }, Token);

        Assert.Single(graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders/{id}/messages?"));
    }

    [Fact]
    public async Task Should_report_a_missing_role_or_path()
    {
        using var graph = new GraphFixture().WithFolders();

        var role = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Mailbox.SearchAsync(new MailSearch { Folder = "archive" }, Token));
        var path = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Mailbox.SearchAsync(new MailSearch { Folder = "Clients/Nowhere" }, Token));

        Assert.Equal((EmailErrorCode.FolderNotFound, "This account has no archive folder: list them with email_folders."), (role.Code, role.Message));
        Assert.Equal((EmailErrorCode.FolderNotFound, "The folder 'Clients/Nowhere' does not exist: list them with email_folders."), (path.Code, path.Message));
    }

    [Fact]
    public async Task Should_read_a_message_as_MIME_with_its_folder_and_marks()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/messages/AAMk/1=?$select", FakeHttpResponse.Json("""{"isRead":true,"flag":{"flagStatus":"flagged"},"parentFolderId":"id-news"}"""));
        graph.Http.Map(HttpMethod.Get, $"{Base}me/messages/AAMk/1=/$value", new FakeHttpResponse(HttpStatusCode.OK, MimeSamples.Plain(subject: "From Graph"), "message/rfc822"));

        using var fetched = await graph.Mailbox.GetMessageAsync("graph:AAMk/1=", Token);

        Assert.Equal(("graph:AAMk/1=", "Inbox/Newsletters", true, true), (fetched.Id, fetched.Folder, fetched.Seen, fetched.Flagged));
        Assert.Equal("From Graph", fetched.Message.Subject);
        Assert.Contains(graph.Http.Requests, request => request.Uri.AbsoluteUri.StartsWith($"{Base}me/messages/AAMk%2F1%3D/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_move_and_return_the_immutable_id_Graph_gives_back()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/messages/m-1/move", FakeHttpResponse.Json("""{"id":"m-1-moved"}"""));
        graph.Http.Map(HttpMethod.Post, $"{Base}me/messages/m-2/move", FakeHttpResponse.Json("{}"));

        var moved = await graph.Mailbox.MoveAsync(["graph:m-1", "graph:m-2"], "Clients/ACME", Token);

        Assert.Equal([new MovedMessage("graph:m-1", "graph:m-1-moved"), new MovedMessage("graph:m-2", "graph:m-2")], moved);
        Assert.All(graph.Http.RequestsTo(HttpMethod.Post, $"{Base}me/messages/"), request =>
        {
            Assert.Equal("application/json", request.ContentType);
            Assert.Equal("""{"destinationId":"id-acme"}""", request.Body);
        });
    }

    [Fact]
    public async Task Should_patch_the_read_and_flag_marks_of_each_message()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Patch, $"{Base}me/messages/", FakeHttpResponse.Json("{}"));

        var updated = await graph.Mailbox.SetFlagsAsync(["graph:m-1", "graph:m-2"], seen: true, flagged: false, Token);

        Assert.Equal(2, updated);
        var patches = graph.Http.RequestsTo(HttpMethod.Patch, $"{Base}me/messages/");
        Assert.Equal([$"{Base}me/messages/m-1", $"{Base}me/messages/m-2"], patches.Select(p => p.Uri.AbsoluteUri));
        Assert.All(patches, patch => Assert.Equal("""{"isRead":true,"flag":{"flagStatus":"notFlagged"}}""", patch.Body));
    }

    [Fact]
    public async Task Should_patch_only_the_marks_asked_for()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Patch, $"{Base}me/messages/", FakeHttpResponse.Json("{}"));

        await graph.Mailbox.SetFlagsAsync(["graph:m-1"], seen: null, flagged: true, Token);

        Assert.Equal("""{"flag":{"flagStatus":"flagged"}}""", Assert.Single(graph.Http.RequestsTo(HttpMethod.Patch, $"{Base}me/messages/")).Body);
    }

    [Fact]
    public async Task Should_move_deleted_messages_to_Deleted_Items()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/messages/m-1?$select=parentFolderId", FakeHttpResponse.Json("""{"parentFolderId":"id-inbox"}"""));
        graph.Http.Map(HttpMethod.Post, $"{Base}me/messages/m-1/move", FakeHttpResponse.Json("""{"id":"m-1"}"""));

        var outcome = await graph.Mailbox.DeleteAsync(["graph:m-1"], permanent: false, Token);

        Assert.Equal(new DeleteOutcome(1, false, "Deleted Items"), outcome);
        Assert.Equal("""{"destinationId":"id-trash"}""", Assert.Single(graph.Http.RequestsTo(HttpMethod.Post, $"{Base}me/messages/m-1/move")).Body);
    }

    [Fact]
    public async Task Should_refuse_a_trash_delete_of_a_message_already_in_Deleted_Items()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/messages/m-1?$select=parentFolderId", FakeHttpResponse.Json("""{"parentFolderId":"id-trash"}"""));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Mailbox.DeleteAsync(["graph:m-1"], permanent: false, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.StartsWith("This message is already in Deleted Items: pass `permanent: true`", error.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Http.RequestsTo(HttpMethod.Post, Base));
    }

    [Fact]
    public async Task Should_delete_for_good_with_permanentDelete_and_nothing_else()
    {
        using var graph = new GraphFixture();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/messages/", new FakeHttpResponse(HttpStatusCode.NoContent, string.Empty));

        var outcome = await graph.Mailbox.DeleteAsync(["graph:m-1", "graph:m-2"], permanent: true, Token);

        Assert.Equal(new DeleteOutcome(2, true, null), outcome);
        Assert.Equal(
            [$"{Base}me/messages/m-1/permanentDelete", $"{Base}me/messages/m-2/permanentDelete"],
            graph.Http.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Should_save_a_draft_posted_as_base64_MIME()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/messages", FakeHttpResponse.Json("""{"id":"draft-1"}"""));
        using var draft = MimeSamples.Load(MimeSamples.Plain(subject: "Draft via Graph"));

        var (id, folder) = await graph.Mailbox.SaveDraftAsync(draft, Token);

        Assert.Equal(("graph:draft-1", "Drafts"), (id, folder));
        var post = Assert.Single(graph.Http.RequestsTo(HttpMethod.Post, $"{Base}me/messages"));
        Assert.Equal("text/plain", post.ContentType);
        Assert.Contains("Subject: Draft via Graph", Encoding.UTF8.GetString(Convert.FromBase64String(post.Body!)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_create_a_child_folder_under_its_existing_parent()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/mailFolders/id-acme/childFolders",
            FakeHttpResponse.Json(GraphFixture.Folder("id-2026", "2026", 0, 0, 0)));

        var (folder, created) = await graph.Mailbox.CreateFolderAsync("Clients/ACME/2026", Token);

        Assert.True(created);
        Assert.Equal(new MailFolderInfo("Clients/ACME/2026", "2026", null, 0, 0), folder);
        Assert.Equal("""{"displayName":"2026"}""", Assert.Single(graph.Http.RequestsTo(HttpMethod.Post, Base)).Body);
    }

    [Fact]
    public async Task Should_create_missing_parents_from_the_root()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/mailFolders/id-projects/childFolders", FakeHttpResponse.Json(GraphFixture.Folder("id-q4", "Q4", 0, 0, 0)));
        graph.Http.Map(HttpMethod.Post, $"{Base}me/mailFolders", FakeHttpResponse.Json(GraphFixture.Folder("id-projects", "Projects", 0, 0, 0)));

        var (folder, created) = await graph.Mailbox.CreateFolderAsync("Projects/Q4", Token);

        Assert.True(created);
        Assert.Equal("Projects/Q4", folder.Path);
        Assert.Equal(
            [$"{Base}me/mailFolders", $"{Base}me/mailFolders/id-projects/childFolders"],
            graph.Http.RequestsTo(HttpMethod.Post, Base).Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Should_answer_created_false_without_posting_When_the_folder_exists()
    {
        using var graph = new GraphFixture().WithFolders();

        var (folder, created) = await graph.Mailbox.CreateFolderAsync("clients/ACME", Token);

        Assert.False(created);
        Assert.Equal("Clients/ACME", folder.Path);
        Assert.Empty(graph.Http.RequestsTo(HttpMethod.Post, Base));
    }

    [Fact]
    public async Task Should_rename_a_folder_and_keep_its_parent_path()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Patch, $"{Base}me/mailFolders/id-acme", FakeHttpResponse.Json(GraphFixture.Folder("id-acme", "Acme Corp", 0, 2, 1)));

        var folder = await graph.Mailbox.RenameFolderAsync("Clients/ACME", " Acme Corp ", Token);

        Assert.Equal(new MailFolderInfo("Clients/Acme Corp", "Acme Corp", null, 2, 1), folder);
        Assert.Equal("""{"displayName":"Acme Corp"}""", Assert.Single(graph.Http.RequestsTo(HttpMethod.Patch, Base)).Body);
    }

    [Theory]
    [InlineData("inbox", "Other", "The inbox folder is a system folder and cannot be renamed.")]
    [InlineData("Deleted Items", "Bin", "The trash folder is a system folder and cannot be renamed.")]
    [InlineData("Clients", "A/B", "`new_name` is a single folder name, without '/'.")]
    public async Task Should_refuse_to_rename_a_system_folder_or_to_a_path(string path, string newName, string message)
    {
        using var graph = new GraphFixture().WithFolders();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Mailbox.RenameFolderAsync(path, newName, Token));

        Assert.Equal((EmailErrorCode.InvalidRequest, message), (error.Code, error.Message));
        Assert.Empty(graph.Http.RequestsTo(HttpMethod.Patch, Base));
    }

    [Fact]
    public async Task Should_reuse_the_folder_tree_for_a_minute()
    {
        using var graph = new GraphFixture().WithFolders();
        graph.Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/messages?", FakeHttpResponse.Json("""{"value":[]}"""));

        await graph.Mailbox.SearchAsync(new MailSearch(), Token);
        graph.Credentials.Time.Advance(TimeSpan.FromSeconds(50));
        await graph.Mailbox.SearchAsync(new MailSearch(), Token);
        var loadsBefore = graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders?$top=250").Count;
        graph.Credentials.Time.Advance(TimeSpan.FromSeconds(20));
        await graph.Mailbox.SearchAsync(new MailSearch(), Token);

        Assert.Equal(1, loadsBefore);
        Assert.Equal(2, graph.Http.RequestsTo(HttpMethod.Get, $"{Base}me/mailFolders?$top=250").Count);
    }

    [Fact]
    public async Task Should_leave_the_Sent_copy_to_sendMail_and_declare_every_capability()
    {
        using var graph = new GraphFixture();
        using var message = MimeSamples.Load(MimeSamples.Plain());

        await graph.Mailbox.AppendToSentAsync(message, Token);

        Assert.Empty(graph.Http.Requests);
        Assert.Equal(
            MailboxCapabilities.Folders | MailboxCapabilities.Move | MailboxCapabilities.Flags | MailboxCapabilities.Drafts
            | MailboxCapabilities.BodySearch | MailboxCapabilities.RawQuery | MailboxCapabilities.Trash,
            graph.Mailbox.Capabilities);
    }

    private static Dictionary<string, string> QueryOf(RecordedRequest request) =>
        request.Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
}
