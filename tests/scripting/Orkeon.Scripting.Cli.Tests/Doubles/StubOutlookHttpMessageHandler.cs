using System.Net;
using System.Text;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// The Microsoft endpoints an Outlook e-mail account talks to, canned: the identity platform's
/// device-code and token endpoints, and the Graph mail folders (an inbox of 12 messages, 3 of
/// them unread, and a Sent Items folder). Records every request.
/// </summary>
internal sealed class StubOutlookHttpMessageHandler : HttpMessageHandler
{
    private const string DeviceCode =
        """{ "device_code": "device-1", "user_code": "WXYZ-1234", "verification_uri": "https://microsoft.com/devicelogin", "expires_in": 900, "interval": 1 }""";

    private const string Tokens =
        """{ "token_type": "Bearer", "access_token": "access-1", "refresh_token": "refresh-1", "expires_in": 3600, "scope": "https://graph.microsoft.com/Mail.ReadWrite" }""";

    private const string Folders = """
        { "value": [
            { "id": "folder-inbox", "displayName": "Inbox", "childFolderCount": 0, "totalItemCount": 12, "unreadItemCount": 3 },
            { "id": "folder-sentitems", "displayName": "Sent Items", "childFolderCount": 0, "totalItemCount": 5, "unreadItemCount": 0 }
        ] }
        """;

    /// <summary>The paths requested, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>The request bodies, in order (empty for a request without one).</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>What Graph answers: any other status than 200 fails every Graph call with it.</summary>
    public HttpStatusCode GraphStatus { get; set; } = HttpStatusCode.OK;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add(uri.AbsolutePath);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        if (uri.Host == "graph.microsoft.com")
        {
            if (GraphStatus != HttpStatusCode.OK)
                return Json("""{ "error": { "code": "ServiceUnavailable", "message": "The service is temporarily unavailable." } }""", GraphStatus);

            // A well-known folder answers with its id; the folder list with the two folders.
            var wellKnown = uri.AbsolutePath["/v1.0/me/mailFolders".Length..].TrimStart('/');
            return Json(wellKnown.Length == 0 ? Folders : $$"""{ "id": "folder-{{wellKnown}}" }""");
        }

        return Json(uri.AbsolutePath.EndsWith("/devicecode", StringComparison.Ordinal) ? DeviceCode : Tokens);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
