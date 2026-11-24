using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Tests.Doubles;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Providers;

/// <summary>The application account: a token of its own, the allowed places alone, and nobody's view.</summary>
public sealed class TeamsApplicationTests : IDisposable
{
    private const string Secret = "s3cret-canary";
    private const string Issued = """{"access_token":"app-1","expires_in":3599}""";
    private const string Team = "00000000-0000-0000-0000-00000000000a";
    private const string Gone = "00000000-0000-0000-0000-00000000000f";
    private const string Channel = "19:general@thread.tacv2";
    private const string Chat = "19:weekly@thread.v2";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("orkeon-chat-app-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Delete(recursive: true);

    [Theory]
    [InlineData("Global", "https://graph.microsoft.com/.default", "login.microsoftonline.com")]
    [InlineData("UsGov", "https://graph.microsoft.us/.default", "login.microsoftonline.us")]
    [InlineData("UsGovDoD", "https://dod-graph.microsoft.us/.default", "login.microsoftonline.us")]
    public async Task Should_ask_for_a_token_of_its_cloud_with_the_secret_and_keep_it_in_memory(string cloud, string scope, string host)
    {
        var options = TestChatAccounts.Application($"chat:{Chat}");
        options.Cloud = Enum.Parse<ChatCloud>(cloud);
        using var fixture = Create(options);

        Assert.Equal("app-1", await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token));
        Assert.Equal("app-1", await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token));

        var request = Assert.Single(fixture.Graph.TokenRequests);
        Assert.Equal(host, request.Uri.Host);
        Assert.Equal(("client_credentials", scope, Secret), (request.Form["grant_type"], request.Form["scope"], request.Form["client_secret"]));
        Assert.Equal((0, 0), (fixture.Store.Reads, fixture.Store.Writes));
        Assert.Empty(fixture.Store.Tokens);
    }

    [Fact]
    public async Task Should_ask_for_another_token_when_one_lapses_or_is_refused()
    {
        using var fixture = Create();
        await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token);

        fixture.Graph.Identity("token", """{"access_token":"app-2","expires_in":3599}""");
        Assert.Equal("app-2", await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token, refused: "app-1"));
        fixture.Time.Advance(TimeSpan.FromHours(1));
        await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token);

        Assert.Equal(3, fixture.Graph.TokenRequests.Count);
    }

    [Fact]
    public async Task Should_sign_in_with_a_certificate()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=t", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100));
        using var fixture = Create(WithFile(certificate: true), WriteFile(certificate.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem()));

        await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token);

        var form = Assert.Single(fixture.Graph.TokenRequests).Form;
        Assert.Equal(3, form["client_assertion"].Split('.').Length);
        Assert.False(form.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task Should_read_the_federated_token_again_for_each_request()
    {
        var path = WriteFile("jwt.one.x\n");
        using var fixture = Create(WithFile(certificate: false), path);
        await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token);

        await File.WriteAllTextAsync(path, "jwt.two.x", Token);
        await fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token, refused: "app-1");

        Assert.Equal(["jwt.one.x", "jwt.two.x"], fixture.Graph.TokenRequests.Select(request => request.Form["client_assertion"]));
    }

    [Theory]
    [InlineData("missing", "names a file that does not exist")]
    [InlineData("relative", "is not an absolute path")]
    [InlineData("folder", "names a file that cannot be read")]
    [InlineData("empty", "names an empty file, or one that is not a regular file")]
    [InlineData("pipe", "names an empty file, or one that is not a regular file")]
    [InlineData("linked-pipe", "names an empty file, or one that is not a regular file")]
    [InlineData("large", "names a file too large for a credential")]
    [InlineData("not-a-key", "is not usable")]
    [InlineData("unset", "which is not set")]
    public async Task Should_say_what_is_wrong_with_a_credential_file_without_its_path_or_content(string wrong, string problem)
    {
        var path = wrong switch
        {
            "missing" => Path.Join(_folder.FullName, "canary-none"),
            "relative" => "canary-relative.pem",
            "folder" => _folder.FullName,
            "empty" => WriteFile(string.Empty),
            "pipe" => Pipe(),
            "linked-pipe" => File.CreateSymbolicLink(Path.Join(_folder.FullName, "canary-link"), Pipe()).FullName,
            "large" => WriteFile(new string('c', 70_000)),
            "not-a-key" => WriteFile("canary-content"),
            _ => " ",
        };
        using var fixture = Create(WithFile(certificate: true), path);

        var status = Assert.Single(await fixture.Administration.ListAsync(Token));
        var refused = await Assert.ThrowsAsync<ChatToolException>(() => fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token));

        Assert.False(status.Ready);
        Assert.Equal(refused.Message, status.CredentialProblem);
        Assert.Equal(ChatErrorCode.InvalidConfiguration, refused.Code);
        Assert.Contains(problem, refused.Message, StringComparison.Ordinal);
        Assert.Contains(TestChatAccounts.ApplicationVariable, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("canary", refused.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_folder.FullName, refused.ToString(), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.TokenRequests);
    }

    [Fact]
    public async Task Should_not_repeat_a_secret_the_provider_refuses()
    {
        using var fixture = Create();
        fixture.Graph.Identity("token", $$"""{"error":"invalid_client","error_description":"AADSTS7000215 {{Secret}}"}""");

        var refused = await Assert.ThrowsAsync<ChatToolException>(() => fixture.Credentials.GetAccessTokenAsync(fixture.Account, Token));

        Assert.Equal(ChatErrorCode.InvalidConfiguration, refused.Code);
        Assert.Contains("AADSTS7000215", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, refused.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("chat login", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_list_the_allowed_teams_alone_past_a_wrong_one_and_read_each_once()
    {
        using var fixture = Create(TestChatAccounts.Application($"team:{Gone}", $"team:{Team}/channel:{Channel}", $"team:{Team}/channel:19:other@thread.tacv2"));
        fixture.Graph.Get($"teams/{Team}", Samples.Named(Team, "Platform"));

        var result = await fixture.ResultAsync("chat_list", ("kind", "teams"));
        await fixture.ResultAsync("chat_list", ("kind", "teams"));

        Assert.Equal(1, Count(result));
        Assert.Single(fixture.Graph.To(HttpMethod.Get, $"teams/{Team}"));
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, "teams"));
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, "me/joinedTeams"));
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, "me"));
    }

    [Fact]
    public async Task Should_list_the_allowed_chats_alone()
    {
        using var fixture = Create(TestChatAccounts.Application($"chat:{Chat}", "chat:19:gone@thread.v2"));
        fixture.Graph.Get($"chats/{Chat}", Samples.Chat(Chat, "group", "Weekly", (Samples.Self, "Me"), (Samples.Ada, "Ada")));

        var result = await fixture.ResultAsync("chat_list", ("kind", "chats"));

        Assert.Equal(1, Convert.ToInt32(result["count"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, "me/chats"));
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, "me"));
    }

    [Fact]
    public async Task Should_list_and_check_the_allowed_chats_a_page_at_a_time()
    {
        string[] ids = ["19:a@thread.v2", "19:b@thread.v2", "19:c@thread.v2"];
        using var fixture = Create(TestChatAccounts.Application([.. ids.Select(id => $"chat:{id}")]));
        foreach (var id in ids)
            fixture.Graph.Get($"chats/{id}", Samples.Chat(id, "group", "Weekly", (Samples.Ada, "Ada")));

        var first = await fixture.ResultAsync("chat_list", ("kind", "chats"), ("limit", 2));
        Assert.Equal((2, 2), (Count(first), ids.Sum(id => fixture.Graph.To(HttpMethod.Get, $"chats/{id}").Count)));
        var last = await fixture.ResultAsync("chat_list", ("kind", "chats"), ("limit", 2), ("cursor", first["next_cursor"]!));

        Assert.Equal(1, Count(last));
        Assert.Null(last.GetValueOrDefault("next_cursor"));
        Assert.True(Assert.Single((await fixture.Administration.CheckAsync("work", Token)).Rights).Ready);
    }

    [Fact]
    public async Task Should_read_an_allowed_chat_and_channel_without_looking_the_author_up()
    {
        using var fixture = Create(TestChatAccounts.Application($"chat:{Chat}", $"team:{Team}/channel:{Channel}"));
        fixture.Graph.Get($"chats/{Chat}/messages", Samples.List(Samples.Message("1", "bonjour")))
            .Get($"teams/{Team}/channels/{Channel}/messages", Samples.List(Samples.Message("2", "salut")));

        var chat = await fixture.ResultAsync("chat_read", ("place", $"chat:{Chat}"));
        var channel = await fixture.ResultAsync("chat_read", ("place", $"team:{Team}/channel:{Channel}"));

        Assert.Equal((1, 1), (Count(chat), Count(channel)));
        Assert.All(fixture.Handler.Requests, request => Assert.DoesNotContain("/users/", request.Uri.AbsolutePath, StringComparison.Ordinal));
        Assert.All(fixture.Handler.Requests.Where(request => request.Method == HttpMethod.Get), request => Assert.Equal("Bearer app-1", request.Authorization));
    }

    [Fact]
    public async Task Should_refuse_a_place_outside_the_allowed_ones_before_reading_it()
    {
        using var fixture = Create(TestChatAccounts.Application($"chat:{Chat}"));

        Assert.Contains("is not a place", await fixture.ErrorAsync("chat_read", ("place", "chat:19:other@thread.v2")), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("Ada")]
    [InlineData("ada@example.org")]
    [InlineData(Samples.Ada)]
    public async Task Should_look_nobody_up(string person)
    {
        using var fixture = Create();

        Assert.Contains("is an application, which looks nobody up", await fixture.ErrorAsync("chat_members", ("person", person)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_refuse_what_belongs_to_a_user()
    {
        using var fixture = Create();
        var provider = fixture.Session.Provider;
        var chat = new ChatTarget { ChatId = Chat };
        var text = new ChatOutgoing("x", [], ReplyTo: null);

        Func<Task>[] calls =
        [
            () => provider.GetSelfAsync(Token), () => provider.FindDirectChatAsync(Samples.Ada, Token), () => provider.OpenDirectChatAsync(Samples.Ada, Token),
            () => provider.SearchAsync(new ChatSearchQuery(["x"], null, null, 0, 5), Token),
            () => provider.SendAsync(chat, text, Token),
            () => provider.EditAsync(chat, null, "1", text, Token),
            () => provider.DeleteAsync(chat, null, "1", Token),
            () => provider.ReactAsync(chat, "1", "x", remove: false, Token),
            () => provider.FindMeetingAsync("https://teams.microsoft.com/l/x", organizer: null, Token),
        ];
        foreach (var call in calls)
            Assert.Equal(ChatErrorCode.NotSupportedByAccount, (await Assert.ThrowsAsync<ChatToolException>(call)).Code);

        Assert.Contains("does not grant the Send right", await fixture.ErrorAsync("chat_send", ("place", $"chat:{Chat}"), ("text", "x")), StringComparison.Ordinal);
        Assert.Contains("looks nobody up", await fixture.ErrorAsync("chat_read", ("place", "user:ada@example.org")), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("chats/19:weekly@thread.v2/messages", "chat:19:weekly@thread.v2", "Chat.Read.All")]
    [InlineData("teams/00000000-0000-0000-0000-00000000000a/channels/19:general@thread.tacv2/messages", "team:00000000-0000-0000-0000-00000000000a/channel:19:general@thread.tacv2", "ChannelMessage.Read.All")]
    public async Task Should_name_the_application_permission_a_refusal_lacks(string path, string place, string permission)
    {
        using var fixture = Create(TestChatAccounts.Application(place));
        fixture.Graph.Refuse(path);

        var error = await fixture.ErrorAsync("chat_read", ("place", place));

        Assert.Contains($"lacks the {permission} application permission, which a tenant administrator grants", error, StringComparison.Ordinal);
        Assert.DoesNotContain("chat login", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_refuse_a_login_and_have_nothing_to_sign_out()
    {
        using var fixture = Create();

        var refused = await Assert.ThrowsAsync<ChatToolException>(() => fixture.Administration.LoginAsync("work", deviceCode: true, new FakeOAuthLoginInteraction(), Token));
        var logout = await fixture.Administration.LogoutAsync("work", Token);

        Assert.Equal(ChatErrorCode.NotSupportedByAccount, refused.Code);
        Assert.Equal((false, false), (logout.Deleted, logout.Revoked));
        Assert.Contains("is an application", logout.Notice, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_check_the_sign_in_and_the_places()
    {
        using var fixture = Create(TestChatAccounts.Application(
            $"chat:{Chat}", "chat:19:gone@thread.v2", $"team:{Gone}", $"team:{Team}/channel:{Channel}", $"team:{Team}/channel:19:lost@thread.tacv2"));
        fixture.Graph.Get($"chats/{Chat}", Samples.Chat(Chat, "group", "Weekly"))
            .Get($"teams/{Team}", Samples.Named(Team, "Platform"))
            .Get($"teams/{Team}/allChannels", Samples.List(Samples.Named(Channel, "General")))
            .Get($"teams/{Team}/channels", Samples.List(Samples.Named(Channel, "General")));

        var check = await fixture.Administration.CheckAsync("work", Token);

        Assert.Null(check.User);
        Assert.True(check.Contacted);
        var read = Assert.Single(check.Rights);
        Assert.Equal((nameof(ChatRights.Read), false), (read.Right, read.Ready));
        Assert.Equal(4, read.Missing.Count);
        foreach (var wrong in new[] { "chat '19:gone@thread.v2'", $"team '{Gone}'", "channel '19:lost@thread.tacv2'" })
            Assert.Single(read.Missing, line => line.Contains(wrong, StringComparison.Ordinal));
        Assert.Contains(read.Missing, line => line.StartsWith("no message was read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_tell_a_crew_that_an_application_lists_members_of_its_places_alone()
    {
        using var fixture = Create(TestChatAccounts.Application());

        var status = Assert.Single(await fixture.Administration.ListAsync(Token));

        Assert.True(status is { Ready: true, Application: true });
        Assert.Single(new ChatRightsCheck(fixture.Administration).Check(["chat_members"], [status]));
        Assert.Single(new ChatRightsCheck(fixture.Administration).Check(["chat_search"], [status with { ReadPlaces = 1 }]));
    }

    [Fact]
    public async Task Should_read_the_transcript_of_an_allowed_chat_under_who_organizes_it()
    {
        using var fixture = CreateMeeting(Samples.Ada);
        fixture.Graph.Get(Meetings, Samples.List(new JsonObject { ["id"] = "MSo1", ["subject"] = "Weekly", ["chatInfo"] = new JsonObject { ["threadId"] = Chat } }.ToJsonString()))
            .Get(Meetings + "/MSo1/transcripts", Samples.List(new JsonObject { ["id"] = "MSMj" }.ToJsonString()))
            .On(HttpMethod.Get, Meetings + "/MSo1/transcripts/MSMj/content", _ => new FakeHttpResponse(HttpStatusCode.OK, "WEBVTT\n\n00:00:16.246 --> 00:00:17.726\n<v Ada>Ship it.</v>\n", "text/vtt"));

        var listed = await fixture.ResultAsync("chat_meetings", ("place", $"chat:{Chat}"));
        var read = await fixture.ResultAsync("chat_meetings", ("place", $"chat:{Chat}"), ("transcript_id", "MSMj"));

        Assert.Equal("MSMj", Assert.Single(ToolResults.Objects(listed, "transcripts"))["id"]);
        Assert.Equal("[00:00:16] Ada: Ship it.", read["text"]);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Uri.AbsolutePath.Contains("/me", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../me")]
    public async Task Should_refuse_a_meeting_whose_organizer_is_not_told_and_a_join_link(string? organizer)
    {
        using var fixture = CreateMeeting(organizer);

        Assert.Contains("pass `place`", await fixture.ErrorAsync("chat_meetings", ("place", $"chat:{Chat}")), StringComparison.Ordinal);
        Assert.Contains("pass `place`", await fixture.ErrorAsync("chat_meetings", ("join_link", JoinLink)), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Uri.AbsolutePath.Contains("onlineMeetings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_name_the_access_policy_a_refused_meeting_may_lack()
    {
        using var fixture = CreateMeeting(Samples.Ada);
        fixture.Graph.Refuse(Meetings);

        var error = await fixture.ErrorAsync("chat_meetings", ("place", $"chat:{Chat}"));

        Assert.Contains("lacks the OnlineMeetings.Read.All application permission", error, StringComparison.Ordinal);
        Assert.Contains("application access policy", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rights", "Rights holds more than Read and Meetings")]
    [InlineData("granted", "Auth:AdminGranted goes with Auth:Method OAuth2 alone")]
    [InlineData("none", "takes exactly one of")]
    [InlineData("two", "takes exactly one of")]
    [InlineData("value", "Auth:ClientSecretEnvVar is the NAME of an environment variable")]
    [InlineData("any", "Read:AllowedPlaces holds '*'")]
    [InlineData("user", "Read:AllowedPlaces entry 'user:ada@example.org' names a person")]
    [InlineData("team", "Read:AllowedPlaces entry 'team:Platform' names a team by its name")]
    [InlineData("channel", "Read:AllowedPlaces entry 'team:Platform/channel:Alerts' names a team by its name")]
    [InlineData("domain", "Read:AllowedPlaces entry 'user:*@example.org' names a person")]
    [InlineData("tenant-unset", "Auth:Tenant is required")]
    [InlineData("Common", "Auth:Tenant is required")]
    [InlineData("organizations", "Auth:Tenant is required")]
    [InlineData("consumers", "Auth:Tenant consumers")]
    public void Should_refuse_a_wrong_declaration(string wrong, string problem)
    {
        var options = TestChatAccounts.Application($"chat:{Chat}");
        switch (wrong)
        {
            case "rights": options.Rights = ChatRights.Read | ChatRights.Send; break;
            case "granted": options.Auth.AdminGranted.Add("ChannelMessages"); break;
            case "none": options.Auth.ClientSecretEnvVar = null; break;
            case "two": options.Auth.CertificatePathEnvVar = "V"; break;
            case "value": options.Auth.ClientSecretEnvVar = "a~b"; break;
            case "any": options.Read.AllowedPlaces.Add("*"); break;
            case "user": options.Read.AllowedPlaces.Add("user:ada@example.org"); break;
            case "team": options.Read.AllowedPlaces.Add("team:Platform"); break;
            case "channel": options.Read.AllowedPlaces.Add("team:Platform/channel:Alerts"); break;
            case "domain": options.Read.AllowedPlaces.Add("user:*@example.org"); break;
            case "tenant-unset": options.Auth.Tenant = null; break;
            default: options.Auth.Tenant = wrong; break;
        }

        var resolution = ChatAccountResolver.Resolve("work", options);

        Assert.Null(resolution.Account);
        Assert.StartsWith(problem, Assert.Single(resolution.Problems).Replace("Auth:Method ClientCredentials takes", "takes", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("a~b", resolution.Problems[0], StringComparison.Ordinal);
    }

    private const string Meetings = "users/" + Samples.Ada + "/onlineMeetings";
    private const string JoinLink = "https://teams.microsoft.com/l/meetup-join/19%3aweekly%40thread.v2/0";

    private static ChatFixture CreateMeeting(string? organizer)
    {
        var options = TestChatAccounts.Application($"chat:{Chat}");
        options.Rights = ChatRights.Read | ChatRights.Meetings;
        var fixture = Create(options);
        var meeting = new JsonObject { ["joinWebUrl"] = JoinLink };
        if (organizer is not null)
            meeting["organizer"] = new JsonObject { ["id"] = organizer };
        fixture.Graph.Get($"chats/{Chat}", new JsonObject { ["id"] = Chat, ["onlineMeetingInfo"] = meeting }.ToJsonString());
        return fixture;
    }

    private static int Count(Dictionary<string, object?> result) => Convert.ToInt32(result["count"], System.Globalization.CultureInfo.InvariantCulture);

    private static ChatFixture Create(ChatAccountOptions? options = null, string credential = Secret)
    {
        var fixture = new ChatFixture(options ?? TestChatAccounts.Application($"chat:{Chat}"));
        fixture.Variables[TestChatAccounts.ApplicationVariable] = credential;
        fixture.Graph.Identity("token", Issued);
        return fixture;
    }

    private static ChatAccountOptions WithFile(bool certificate)
    {
        var options = TestChatAccounts.Application($"chat:{Chat}");
        options.Auth.ClientSecretEnvVar = null;
        if (certificate)
            options.Auth.CertificatePathEnvVar = TestChatAccounts.ApplicationVariable;
        else
            options.Auth.FederatedTokenFileEnvVar = TestChatAccounts.ApplicationVariable;
        return options;
    }

    private string Pipe()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A named pipe is no file on Windows.");
        var path = Path.Join(_folder.FullName, "canary-pipe");
        using var made = System.Diagnostics.Process.Start("mkfifo", [path]);
        made.WaitForExit();
        Assert.Equal(0, made.ExitCode);
        return path;
    }

    private string WriteFile(string content)
    {
        var path = Path.Join(_folder.FullName, "credential");
        File.WriteAllText(path, content);
        return path;
    }
}
