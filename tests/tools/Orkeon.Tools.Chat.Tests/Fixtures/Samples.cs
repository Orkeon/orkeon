using System.Text.Json.Nodes;

namespace Orkeon.Tools.Chat.Tests.Fixtures;

/// <summary>Graph-shaped JSON.</summary>
internal static class Samples
{
    public const string Self = "11111111-1111-1111-1111-111111111111";
    public const string Ada = "22222222-2222-2222-2222-222222222222";
    public const string Guest = "33333333-3333-3333-3333-333333333333";
    public const string Platform = "44444444-4444-4444-4444-444444444444";
    public const string Sales = "55555555-5555-5555-5555-555555555555";
    public const string Builds = "19:builds@thread.tacv2";
    public const string General = "19:general@thread.tacv2";
    public const string Deals = "19:deals@thread.tacv2";
    public const string Group = "19:meeting_abc@thread.v2";
    public const string Direct = "19:" + Self + "_" + Ada + "@unq.gbl.spaces";
    public const string GuestDirect = "19:" + Self + "_" + Guest + "@unq.gbl.spaces";

    public const string BuildsPlace = "team:" + Platform + "/channel:" + Builds;

    public const string BuildsMessages = "teams/" + Platform + "/channels/" + Builds + "/messages";

    /// <summary>A collection.</summary>
    public static string List(params string[] items) => Page(null, items);

    /// <summary>A collection, with its next link.</summary>
    public static string Page(string? next, params string[] items)
    {
        var page = new JsonObject { ["value"] = new JsonArray(items.Select(item => JsonNode.Parse(item)).ToArray()) };
        if (next is not null)
            page["@odata.nextLink"] = next;
        return page.ToJsonString();
    }

    public static string Person(string id, string name, string? upn, string? mail = null, string? type = "Member", string? creation = null) =>
        new JsonObject { ["id"] = id, ["displayName"] = name, ["userPrincipalName"] = upn, ["mail"] = mail, ["userType"] = type, ["creationType"] = creation }.ToJsonString();

    public static string AdaPerson => Person(Ada, "Ada", "ada@example.org", "ada@example.org");

    public static string GuestPerson(string upn = "gus_partner.test#EXT#@example.org", string? mail = "gus@partner.test") => Person(Guest, "Gus", upn, mail, "Guest", "Invitation");

    public static string Named(string id, string name) => new JsonObject { ["id"] = id, ["displayName"] = name }.ToJsonString();

    public static string Channel(string id, string name, string membership = "standard") =>
        new JsonObject { ["id"] = id, ["displayName"] = name, ["membershipType"] = membership }.ToJsonString();

    public static string Chat(string id, string kind, string? topic, params (string Id, string Name)[] members) => new JsonObject
    {
        ["id"] = id,
        ["chatType"] = kind,
        ["topic"] = topic,
        ["members"] = new JsonArray(members.Select(member => JsonNode.Parse(Member(member.Id, member.Name))).ToArray()),
    }.ToJsonString();

    public static string DirectChat => Chat(Direct, "oneOnOne", null, (Self, "Me"), (Ada, "Ada"));

    public static string GroupChat => Chat(Group, "group", "Weekly", (Self, "Me"), (Ada, "Ada"));

    public static string Member(string id, string name, string? email = null, params string[] roles) => new JsonObject
    {
        ["@odata.type"] = "#microsoft.graph.aadUserConversationMember",
        ["id"] = "membership",
        ["userId"] = id,
        ["displayName"] = name,
        ["email"] = email,
        ["roles"] = new JsonArray(roles.Select(role => (JsonNode?)JsonValue.Create(role)).ToArray()),
    }.ToJsonString();

    /// <summary>A chatMessage, edited by <paramref name="edit"/>.</summary>
    public static string Message(string id, string text, string created = "2026-09-30T10:00:00Z", Action<JsonObject>? edit = null)
    {
        var message = new JsonObject
        {
            ["id"] = id,
            ["replyToId"] = null,
            ["messageType"] = "message",
            ["createdDateTime"] = created,
            ["lastEditedDateTime"] = null,
            ["deletedDateTime"] = null,
            ["policyViolation"] = null,
            ["from"] = new JsonObject
            {
                ["application"] = null,
                ["user"] = new JsonObject { ["id"] = Ada, ["displayName"] = "Ada", ["userIdentityType"] = "aadUser" },
            },
            ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = $"<p>{text}</p>" },
            ["attachments"] = new JsonArray(),
            ["mentions"] = new JsonArray(),
            ["reactions"] = new JsonArray(),
        };
        edit?.Invoke(message);
        return message.ToJsonString();
    }

    /// <summary>Gives the teams, channels, people and chats of the samples.</summary>
    public static GraphStub Directory(this GraphStub graph) => graph
        .Get("me/joinedTeams", List(Named(Platform, "Platform"), Named(Sales, "Sales")))
        .Get($"teams/{Platform}/channels", List(Channel(Builds, "builds"), Channel(General, "General")))
        .Get($"teams/{Sales}/channels", List(Channel(Deals, "builds")))
        .Get($"users/{Ada}", AdaPerson)
        .Get("users/ada@example.org", AdaPerson)
        .Get($"users/{Guest}", GuestPerson())
        .Get($"chats/{Direct}", DirectChat)
        .Get($"chats/{GuestDirect}", Chat(GuestDirect, "oneOnOne", null, (Self, "Me"), (Guest, "Gus")))
        .Get($"chats/{Group}", GroupChat);
}
