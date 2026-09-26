using System.Net;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes.Graph;
using Orkeon.Tools.Email.Tests.Doubles;

namespace Orkeon.Tools.Email.Tests.Fixtures;

/// <summary>
/// An Outlook account on Microsoft Graph with a fresh token, a <see cref="GraphClient"/> and a
/// <see cref="GraphMailbox"/> over the route-based HTTP double, and a mailbox tree to map:
/// Inbox (with Newsletters), Sent Items, Drafts, Deleted Items, Junk Email, Clients (with ACME).
/// There is no Archive: Graph answers 404 for that well-known name.
/// </summary>
internal sealed class GraphFixture : IDisposable
{
    /// <summary>The Graph base address.</summary>
    public const string Base = "https://graph.microsoft.com/v1.0/";

    /// <summary>The access token every request must carry.</summary>
    public const string AccessToken = "graph-access-token";

    /// <summary>Creates the fixture.</summary>
    public GraphFixture()
    {
        Account = TestAccounts.Resolve("hotmail", TestAccounts.Outlook(TestAccounts.AllRights));
        Credentials.SeedFreshToken(Account, AccessToken);
        Client = new GraphClient(Credentials.Http, Account, Credentials.Provider);
        Mailbox = new GraphMailbox(Client, Credentials.Time);
    }

    /// <summary>Clock, token store and the HTTP double.</summary>
    public CredentialsFixture Credentials { get; } = new();

    /// <summary>The HTTP double standing for Graph.</summary>
    public FakeHttpMessageHandler Http => Credentials.Handler;

    /// <summary>The account.</summary>
    public ResolvedEmailAccount Account { get; }

    /// <summary>The Graph client.</summary>
    public GraphClient Client { get; }

    /// <summary>The mailbox under test.</summary>
    public GraphMailbox Mailbox { get; }

    /// <summary>Maps the well-known folders and the folder tree.</summary>
    public GraphFixture WithFolders()
    {
        foreach (var (wellKnown, id) in new[] { ("inbox", "id-inbox"), ("sentitems", "id-sent"), ("drafts", "id-drafts"), ("deleteditems", "id-trash"), ("junkemail", "id-junk") })
            Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/{wellKnown}?$select=id", FakeHttpResponse.Json($$"""{"id":"{{id}}"}"""));

        Http.Map(HttpMethod.Get, $"{Base}me/mailFolders?$top=250", FakeHttpResponse.Json($$"""
            {"value":[
              {{Folder("id-inbox", "Inbox", 1, 5, 2)}},
              {{Folder("id-sent", "Sent Items", 0, 7, 0)}},
              {{Folder("id-drafts", "Drafts", 0, 1, 0)}},
              {{Folder("id-trash", "Deleted Items", 0, 3, 3)}},
              {{Folder("id-junk", "Junk Email", 0, 0, 0)}},
              {{Folder("id-clients", "Clients", 1, 0, 0)}}
            ]}
            """));
        Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-inbox/childFolders?", FakeHttpResponse.Json($$"""{"value":[{{Folder("id-news", "Newsletters", 0, 40, 12)}}]}"""));
        Http.Map(HttpMethod.Get, $"{Base}me/mailFolders/id-clients/childFolders?", FakeHttpResponse.Json($$"""{"value":[{{Folder("id-acme", "ACME", 0, 2, 1)}}]}"""));
        return this;
    }

    /// <summary>A folder as Graph lists it.</summary>
    public static string Folder(string id, string name, int children, int total, int unread) =>
        $$"""{"id":"{{id}}","displayName":"{{name}}","parentFolderId":"root","childFolderCount":{{children}},"totalItemCount":{{total}},"unreadItemCount":{{unread}}}""";

    /// <summary>A message summary as Graph lists it.</summary>
    public static string Summary(
        string id, string subject, bool isRead = false, bool flagged = false, bool attachments = false,
        string received = "2026-09-20T08:00:00Z", string name = "Alice Martin", string address = "alice@example.com", string preview = "Hello there") =>
        $$$"""{"id":"{{{id}}}","subject":"{{{subject}}}","from":{"emailAddress":{"name":"{{{name}}}","address":"{{{address}}}"}},"receivedDateTime":"{{{received}}}","isRead":{{{(isRead ? "true" : "false")}}},"flag":{"flagStatus":"{{{(flagged ? "flagged" : "notFlagged")}}}"},"hasAttachments":{{{(attachments ? "true" : "false")}}},"bodyPreview":"{{{preview}}}"}""";

    /// <summary>A Graph error body.</summary>
    public static FakeHttpResponse Error(HttpStatusCode status, string code, string message, TimeSpan? retryAfter = null) =>
        new(status, $$$"""{"error":{"code":"{{{code}}}","message":"{{{message}}}"}}""", RetryAfter: retryAfter);

    /// <inheritdoc />
    public void Dispose() => Credentials.Dispose();
}
