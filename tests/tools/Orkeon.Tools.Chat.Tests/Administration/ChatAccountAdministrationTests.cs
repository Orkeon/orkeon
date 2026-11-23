using System.Net;
using System.Web;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Administration;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Tests.Doubles;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Administration;

/// <summary>The operator's commands: list, sign in, sign out, check.</summary>
public sealed class ChatAccountAdministrationTests
{
    private const string Signed = """{"access_token":"signed","refresh_token":"signed-refresh","expires_in":3600}""";

    private static readonly ChatTokenSet Wider = new() { AccessToken = "wider", RefreshToken = "wider-refresh", ExpiresAt = DateTimeOffset.MaxValue };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_list_the_accounts_without_any_network()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["*"], send: ["*", "team:x"]), signedIn: false);
        fixture.Options.Accounts["broken"] = new ChatAccountOptions();
        fixture.Options.DefaultAccount = "WORK";

        var statuses = await fixture.Administration.ListAsync(Token);

        Assert.Equal(["broken", "work"], statuses.Select(status => status.Name));
        Assert.Equal(("?", false), (statuses[0].Provider, statuses[0].Ready));
        Assert.Contains("Provider is required: Teams", statuses[0].ConfigurationProblems);
        Assert.Null(statuses[0].TokenEndpoint);
        var work = statuses[1];
        Assert.Equal(("Teams", "Read, Send", "Global", true, false), (work.Provider, work.Rights, work.Cloud, work.IsDefault, work.Ready));
        Assert.Contains("orkeon chat login work", work.Problem, StringComparison.Ordinal);
        Assert.Equal(work.Problem, work.CredentialProblem);
        Assert.Equal((1, 2), (work.ReadPlaces, work.SendPlaces));
        Assert.Empty(fixture.Handler.Requests);

        fixture.SignIn([]);
        Assert.True((await fixture.Administration.ListAsync(Token))[1].Ready);
    }

    [Fact]
    public async Task Should_sign_in_with_the_browser()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(), signedIn: false);
        fixture.Graph.Identity("token", Signed);
        var interaction = new FakeOAuthLoginInteraction();

        await fixture.Administration.LoginAsync("work", deviceCode: false, interaction, Token);

        var query = HttpUtility.ParseQueryString(interaction.AuthorizationUri!.Query);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Null(query["access_type"]);
        Assert.Equal(string.Join(' ', fixture.Account.OAuth.Scopes), query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(("login.microsoftonline.com", "/example.org/oauth2/v2.0/authorize"), (interaction.AuthorizationUri.Host, interaction.AuthorizationUri.AbsolutePath));
        var form = Assert.Single(fixture.Graph.TokenRequests).Form;
        Assert.Equal(("authorization_code", "code-1"), (form["grant_type"], form["code"]));
        Assert.Equal("signed", fixture.Store.Tokens[ChatCredentialProvider.TokenKey(fixture.Account)].AccessToken);
    }

    [Fact]
    public async Task Should_sign_in_with_a_device_code()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read), signedIn: false);
        fixture.Graph.Identity("token", Signed)
            .Identity("devicecode", """{"device_code":"dev","user_code":"ABCD","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":5}""");
        var interaction = new FakeOAuthLoginInteraction();

        await fixture.Administration.LoginAsync("work", deviceCode: true, interaction, Token);

        Assert.Equal("ABCD", interaction.DeviceCode?.UserCode);
        Assert.Null(interaction.AuthorizationUri);
        Assert.Equal(string.Join(' ', fixture.Account.OAuth.Scopes), fixture.Handler.Requests[0].Form["scope"]);
        Assert.Single(fixture.Store.Tokens);
    }

    [Fact]
    public async Task Should_use_the_new_sign_in_at_once()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["*"]));
        fixture.Graph.Identity("token", Signed).Get("me/joinedTeams", Samples.List());
        await fixture.Session.Provider.ListTeamsAsync(Token);

        await fixture.Administration.LoginAsync("work", deviceCode: false, new FakeOAuthLoginInteraction(), Token);
        await fixture.Session.Provider.ListTeamsAsync(Token);

        Assert.Equal(["Bearer first-access", "Bearer signed"], fixture.Graph.To(HttpMethod.Get, "me/joinedTeams").Select(request => request.Authorization));
    }

    [Fact]
    public async Task Should_refuse_a_sign_in_without_a_refresh_token()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(), signedIn: false);
        fixture.Graph.Identity("token", """{"access_token":"signed","expires_in":3600}""");

        var error = await Assert.ThrowsAsync<ChatToolException>(() => fixture.Administration.LoginAsync("work", false, new FakeOAuthLoginInteraction(), Token));

        Assert.Equal(ChatErrorCode.ConsentMissing, error.Code);
        Assert.Contains("offline_access", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Store.Tokens);
    }

    [Fact]
    public async Task Should_tell_a_consent_refused_at_the_sign_in()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(), signedIn: false);
        fixture.Graph.Identity("token", """{"error":"invalid_grant","error_description":"AADSTS65001: no consent"}""");

        var error = await Assert.ThrowsAsync<ChatToolException>(() => fixture.Administration.LoginAsync("work", false, new FakeOAuthLoginInteraction(), Token));

        Assert.Equal(ChatErrorCode.ConsentMissing, error.Code);
    }

    [Fact]
    public async Task Should_delete_the_tokens_at_the_sign_out_and_say_where_the_access_is_removed()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());

        var result = await fixture.Administration.LogoutAsync("work", Token);

        Assert.Equal((true, false), (result.Deleted, result.Revoked));
        Assert.Equal(ChatDefaults.RemoveAccessHint, result.Notice);
        Assert.Contains("Microsoft publishes no token revocation endpoint", result.Notice, StringComparison.Ordinal);
        Assert.Empty(fixture.Store.Tokens);
        Assert.Empty(fixture.Handler.Requests);
        Assert.False((await fixture.Administration.LogoutAsync("work", Token)).Deleted);
    }

    [Fact]
    public async Task Should_delete_the_tokens_of_wider_rights_at_the_sign_out()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read), signedIn: false);
        var wider = ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(TestChatAccounts.Teams()));
        fixture.Store.With(wider, Wider).With("work-x-0123456789ab", Wider);

        Assert.True((await fixture.Administration.LogoutAsync("work", Token)).Deleted);

        Assert.Equal(["work-x-0123456789ab"], fixture.Store.Tokens.Keys);
    }

    [Fact]
    public async Task Should_sign_out_a_declared_account_that_no_longer_resolves()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(), signedIn: false);
        fixture.Options.Accounts["broken"] = new ChatAccountOptions();
        fixture.Options.Accounts["not/valid"] = new ChatAccountOptions();
        fixture.Store.With("broken-0123456789ab", Wider);

        var result = await fixture.Administration.LogoutAsync("BROKEN", Token);

        Assert.Equal((true, false, ChatDefaults.RemoveAccessHint), (result.Deleted, result.Revoked, result.Notice));
        Assert.Empty(fixture.Store.Tokens);
        Assert.Equal(ChatErrorCode.UnknownAccount, (await Assert.ThrowsAsync<ChatToolException>(() => fixture.Administration.LogoutAsync("other", Token))).Code);
        Assert.Equal(ChatErrorCode.InvalidConfiguration, (await Assert.ThrowsAsync<ChatToolException>(() => fixture.Administration.LogoutAsync("not/valid", Token))).Code);
    }

    [Fact]
    public async Task Should_keep_the_tokens_of_the_last_sign_in_only()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());
        fixture.Store.With("work-0123456789ab", Wider).With("work-x-0123456789ab", Wider);

        await fixture.Credentials.StoreAsync(fixture.Account, Wider, Token);

        Assert.Equal([ChatCredentialProvider.TokenKey(fixture.Account), "work-x-0123456789ab"], fixture.Store.Tokens.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_delete_the_tokens_even_When_the_revocation_fails(bool interrupted)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());
        using var http = new HttpClient(fixture.Handler, disposeHandler: false);
        using var interruption = new CancellationTokenSource();
        fixture.Graph.On(HttpMethod.Post, "revoke", _ =>
        {
            if (interrupted)
                interruption.Cancel();
            return GraphStub.Error(HttpStatusCode.ServiceUnavailable, "x");
        });
        var revocable = new ResolvedChatAccount
        {
            Name = fixture.Account.Name,
            Provider = fixture.Account.Provider,
            Rights = fixture.Account.Rights,
            AdminGranted = fixture.Account.AdminGranted,
            Cloud = fixture.Account.Cloud,
            ApiBase = fixture.Account.ApiBase,
            OAuth = fixture.Account.OAuth with { RevocationEndpoint = new Uri("https://login.microsoftonline.com/v1.0/revoke") },
            ReadPlaces = [],
            SendPlaces = [],
        };
        using var sessions = ChatSessions.Over(fixture.Credentials, () => http, fixture.Time);
        var administration = new ChatAccountAdministration(new FakeChatAccountRegistry(revocable), fixture.Credentials, fixture.OAuth, sessions);

        var result = await administration.LogoutAsync("work", interruption.Token);

        Assert.Equal((true, false), (result.Deleted, result.Revoked));
        Assert.StartsWith("The provider did not revoke the token: ", result.Notice, StringComparison.Ordinal);
        Assert.Empty(fixture.Store.Tokens);
    }

    [Fact]
    public async Task Should_check_every_right_and_say_what_is_missing()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(
            ChatRights.Read | ChatRights.Send | ChatRights.React, read: ["team:Platform", "team:Gone"]));
        fixture.SignIn(["https://graph.microsoft.com/Chat.Read", "Team.ReadBasic.All", "channel.readbasic.all", "User.ReadBasic.All", "ChatMessage.Send", "Chat.Create", "ChannelMessage.Send"]);
        fixture.Graph.Directory().Get("me/chats", Samples.List());

        var result = await fixture.Administration.CheckAsync("work", Token);

        Assert.Equal(("work", "me@example.org", false), (result.Account, result.User, result.Ready));
        Assert.Equal(["Read", "Send", "React"], result.Rights.Select(right => right.Right));
        Assert.Contains(result.Rights[0].Missing, line => line.StartsWith("Read:AllowedPlaces entry 'team:Gone' is set aside", StringComparison.Ordinal));
        Assert.Contains(result.Rights[0].Missing, line => line.Contains("ChannelMessage.Read.All: a tenant administrator grants it, then ChannelMessages goes into Auth:AdminGranted", StringComparison.Ordinal));
        Assert.Contains(result.Rights[0].Missing, line => line.Contains("listing a channel's members needs ChannelMember.Read.All", StringComparison.Ordinal));
        Assert.Equal((false, 3), (result.Rights[0].Ready, result.Rights[0].Missing.Count));
        Assert.Equal(["Send:AllowedPlaces is empty, which allows no place"], result.Rights[1].Missing);
        Assert.Equal((true, "no tool uses this right"), (result.Rights[2].Ready, Assert.Single(result.Rights[2].Missing)));
        Assert.Empty(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_check_the_permissions_granted_at_the_sign_in()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Send, send: ["*"]));
        fixture.SignIn(["Team.ReadBasic.All", "Channel.ReadBasic.All", "User.ReadBasic.All", "ChatMessage.Send", "Chat.Create"]);

        var right = Assert.Single((await fixture.Administration.CheckAsync("work", Token)).Rights);

        Assert.False(right.Ready);
        Assert.Equal(["ChannelMessage.Send was not granted at the sign-in: run `orkeon chat login work`"], right.Missing);
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Delete")]
    [InlineData("React")]
    [InlineData("Meetings")]
    public async Task Should_check_an_account_whose_scopes_do_not_open_its_user(string right)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(Enum.Parse<ChatRights>(right)));

        var result = await fixture.Administration.CheckAsync("work", Token);

        Assert.Null(result.User);
        Assert.True(result.Ready);
        Assert.Equal(right, Assert.Single(result.Rights).Right);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_say_the_sign_in_is_missing_When_nothing_else_can_be_checked()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Edit), signedIn: false);

        var refusal = await Assert.ThrowsAsync<ChatToolException>(() => fixture.Administration.CheckAsync("work", Token));

        Assert.Equal(ChatErrorCode.SignInRequired, refusal.Code);
    }

    [Fact]
    public async Task Should_check_the_rights_one_by_one_When_the_user_may_not_be_read()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Send, send: ["*"]));
        fixture.SignIn(["Team.ReadBasic.All"]);
        fixture.Graph.Refuse("me");

        var result = await fixture.Administration.CheckAsync("work", Token);

        Assert.Equal((null, false), (result.User, result.Ready));
        Assert.Contains(result.Rights[0].Missing, line => line.StartsWith("User.ReadBasic.All was not granted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_be_ready_When_everything_is_granted()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["*"], send: ["*"], granted: "ChannelMessages"));
        fixture.Graph.Get("me/chats", Samples.List());

        Assert.True((await fixture.Administration.CheckAsync("work", Token)).Ready);
    }

    [Theory]
    [InlineData("chat_read", "Read", 1, 0, true)]
    [InlineData("chat_read", "Read", 0, 1, false)]
    [InlineData("chat_read", "Send", 1, 1, false)]
    [InlineData("chat_send", "Read, Send", 0, 1, true)]
    [InlineData("chat_send", "Send", 1, 0, false)]
    [InlineData("chat_list", "Send", 0, 1, true)]
    [InlineData("chat_list", "Send", 0, 0, false)]
    [InlineData("chat_members", "Send", 0, 0, true)]
    [InlineData("chat_members", "Edit", 1, 1, false)]
    [InlineData("chat_accounts", "Edit", 0, 0, true)]
    [InlineData("web_search", "Edit", 0, 0, true)]
    public void Should_tell_the_rights_a_crew_lacks(string tool, string rights, int read, int send, bool enough)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());
        var check = new ChatRightsCheck(fixture.Administration);
        var status = new ChatAccountStatus { Name = "work", Provider = "Teams", Rights = rights, Ready = true, ReadPlaces = read, SendPlaces = send };

        Assert.Equal(enough ? 0 : 1, check.Check([tool, tool], [status]).Count);
        Assert.Equal(tool is "chat_accounts" or "web_search" ? 0 : 1, check.Check([tool], [status with { Ready = false }]).Count);
    }

    [Fact]
    public async Task Should_check_a_crew_over_the_accounts_as_they_stand()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read, read: ["*"]));
        var check = new ChatRightsCheck(fixture.Administration);

        Assert.Empty(await check.CheckAsync(["chat_read"], Token));
        Assert.Empty(await check.CheckAsync(["web_search"], Token));
        Assert.Equal(
            ["chat_send needs the Send right with a non-empty Send:AllowedPlaces on a ready chat account, and no account has it. Add it under Orkeon:Tools:Chat:Accounts:<name>:Rights and Send."],
            await check.CheckAsync(["chat_send", "chat_read"], Token));
    }
}
