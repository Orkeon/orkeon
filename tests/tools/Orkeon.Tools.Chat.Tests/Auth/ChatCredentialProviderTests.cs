using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Auth;

/// <summary>The access token of an account: stored, refreshed, never asked of the user here.</summary>
public sealed class ChatCredentialProviderTests : IDisposable
{
    private const string NewTokens = """{"access_token":"second-access","refresh_token":"second-refresh","expires_in":3600,"scope":"Chat.Read"}""";

    private readonly ChatFixture _fixture = new(TestChatAccounts.Teams());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_serve_a_fresh_token_without_any_request()
    {
        Assert.Equal("first-access", await Access());
        Assert.Equal("first-access", await Access());

        Assert.Empty(_fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_ask_for_a_sign_in_once_another_process_signed_out()
    {
        Assert.Equal("first-access", await Access());
        await _fixture.Store.DeleteAccountAsync("work", keptKey: null, Token);

        Assert.Equal(ChatErrorCode.SignInRequired, (await Assert.ThrowsAsync<ChatToolException>(Access)).Code);
        Assert.Empty(_fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_refresh_an_expired_token_once_for_concurrent_calls()
    {
        _fixture.Graph.Identity("token", NewTokens);
        _fixture.Time.Advance(TimeSpan.FromHours(1));

        var tokens = await Task.WhenAll(Access(), Access(), Access());

        Assert.All(tokens, token => Assert.Equal("second-access", token));
        var form = Assert.Single(_fixture.Graph.TokenRequests).Form;
        Assert.Equal(("refresh_token", "first-refresh"), (form["grant_type"], form["refresh_token"]));
        Assert.Equal(string.Join(' ', _fixture.Account.OAuth.Scopes), form["scope"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.Equal("second-refresh", Assert.Single(_fixture.Store.Tokens.Values).RefreshToken);
    }

    [Fact]
    public async Task Should_take_the_token_a_new_sign_in_stored_after_a_refusal()
    {
        await Access();
        _fixture.Store.With(Key(), Tokens("signed-in-again"));

        Assert.Equal("signed-in-again", await _fixture.Credentials.GetAccessTokenAsync(_fixture.Account, Token, "first-access"));
        Assert.Empty(_fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_write_no_refreshed_token_over_a_sign_out_made_meanwhile()
    {
        Refreshing(() => _fixture.Store.Drop(Key()));

        var error = await Assert.ThrowsAsync<ChatToolException>(Access);

        Assert.Equal(ChatErrorCode.SignInRequired, error.Code);
        Assert.Empty(_fixture.Store.Tokens);
        Assert.Equal(0, _fixture.Store.Writes);
        await Assert.ThrowsAsync<ChatToolException>(Access);
    }

    [Fact]
    public async Task Should_take_the_tokens_of_a_sign_in_made_during_a_refresh()
    {
        Refreshing(() => _fixture.Store.With(Key(), Tokens("signed-in-again")));

        Assert.Equal("signed-in-again", await Access());

        Assert.Equal("signed-in-again", Assert.Single(_fixture.Store.Tokens.Values).AccessToken);
        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Fact]
    public async Task Should_sign_out_after_the_refresh_under_way()
    {
        Task<bool>? signOut = null;
        Refreshing(() => signOut = _fixture.Credentials.ForgetAsync("work", _fixture.Account, Token));

        await Access();

        Assert.NotNull(signOut);
        Assert.True(await signOut);
        Assert.Empty(_fixture.Store.Tokens);
        Assert.Equal(ChatErrorCode.SignInRequired, (await Assert.ThrowsAsync<ChatToolException>(Access)).Code);
    }

    [Theory]
    [InlineData("AADSTS65001: The user or administrator has not consented", "ConsentMissing")]
    [InlineData("AADSTS90094: An administrator must consent", "ConsentMissing")]
    [InlineData("AADSTS70008: The refresh token has expired", "SignInRequired")]
    public async Task Should_tell_a_missing_consent_from_an_expired_sign_in(string description, string code)
    {
        _fixture.Graph.Identity("token", $$"""{"error":"invalid_grant","error_description":"{{description}}"}""");
        _fixture.Time.Advance(TimeSpan.FromHours(1));

        var error = await Assert.ThrowsAsync<ChatToolException>(Access);

        Assert.Equal(Enum.Parse<ChatErrorCode>(code), error.Code);
        Assert.Contains("AADSTS", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("first-refresh", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Fact]
    public async Task Should_ask_for_a_sign_in_without_tokens()
    {
        await _fixture.Store.DeleteAsync(Key(), Token);

        var error = await Assert.ThrowsAsync<ChatToolException>(Access);

        Assert.Equal(ChatErrorCode.SignInRequired, error.Code);
        Assert.Equal("Chat account 'work' needs a sign-in: run `orkeon chat login work` in a terminal.", error.Message);
        Assert.Equal(error.Message, await _fixture.Credentials.DiagnoseAsync(_fixture.Account, Token));
    }

    [Fact]
    public async Task Should_be_ready_with_an_expired_token_it_can_refresh_only()
    {
        _fixture.Time.Advance(TimeSpan.FromHours(2));
        Assert.Null(await _fixture.Credentials.DiagnoseAsync(_fixture.Account, Token));

        _fixture.Store.With(Key(), Tokens("x") with { RefreshToken = null, ExpiresAt = _fixture.Time.GetUtcNow() });
        Assert.NotNull(await _fixture.Credentials.DiagnoseAsync(_fixture.Account, Token));
    }

    [Fact]
    public void Should_key_the_tokens_by_what_they_are_valid_for()
    {
        var keys = new[]
        {
            Key(),
            ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(TestChatAccounts.Teams(ChatRights.Read))),
            ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(TestChatAccounts.Teams(granted: "ChannelMessages"))),
            ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(Other(options => options.Auth.Tenant = "other.org"))),
            ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(Other(options => options.Auth.ClientId = TestChatAccounts.Tenant))),
            ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(Other(options => options.Cloud = ChatCloud.UsGov))),
            ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(Other(options => options.Cloud = ChatCloud.UsGovDoD))),
        };

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.StartsWith("work-", key, StringComparison.Ordinal));
        Assert.Equal(Key(), ChatCredentialProvider.TokenKey(TestChatAccounts.Resolve(TestChatAccounts.Teams(send: ["*"]))));
    }

    [Fact]
    public void Should_print_no_token() => Assert.DoesNotContain("printed", Tokens("printed").ToString(), StringComparison.Ordinal);

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    private static ChatAccountOptions Other(Action<ChatAccountOptions> change)
    {
        var options = TestChatAccounts.Teams();
        change(options);
        return options;
    }

    private ChatTokenSet Tokens(string access) => new()
    {
        AccessToken = access,
        RefreshToken = access + "-refresh",
        ExpiresAt = _fixture.Time.GetUtcNow().AddHours(1),
    };

    private void Refreshing(Action meanwhile)
    {
        _fixture.Graph.Identity("token", NewTokens, meanwhile);
        _fixture.Time.Advance(TimeSpan.FromHours(1));
    }

    private string Key() => ChatCredentialProvider.TokenKey(_fixture.Account);

    private Task<string> Access() => _fixture.Credentials.GetAccessTokenAsync(_fixture.Account, Token);
}
