using Microsoft.Extensions.Options;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Administration;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Tests.Doubles;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Administration;

/// <summary>The operator's side: readiness, sign-in, sign-out and the connection check; and the rights check of a crew's tools.</summary>
public sealed class DriveAccountAdministrationTests : IDisposable
{
    private readonly ResolvedDriveAccount _onedrive = TestDriveAccounts.Resolve("pro", TestDriveAccounts.OneDrive());
    private readonly DriveFixture _fixture;
    private readonly DriveToolsOptions _options = new();
    private readonly FakeDriveFactory _drives = new();
    private readonly DriveAccountAdministration _administration;

    public DriveAccountAdministrationTests()
    {
        _fixture = new DriveFixture(_onedrive);
        _options.Accounts["pro"] = TestDriveAccounts.OneDrive();
        _options.Accounts["broken"] = new DriveAccountOptions();
        _administration = new DriveAccountAdministration(new DriveAccountRegistry(Options.Create(_options)), _fixture.Credentials, _fixture.OAuth, _drives);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Should_list_a_ready_account_and_a_broken_one_with_its_problem()
    {
        var statuses = await _administration.ListAsync(Token);

        var broken = statuses.Single(status => status.Name == "broken");
        var pro = statuses.Single(status => status.Name == "pro");
        Assert.Equal(("?", false), (broken.Provider, broken.Ready));
        Assert.Contains("Provider is required", broken.Problem, StringComparison.Ordinal);
        Assert.Equal(("OneDrive", "/Orkeon/agents", true), (pro.Provider, pro.Root, pro.Ready));
    }

    [Fact]
    public async Task Should_say_a_login_is_needed_when_no_token_is_stored()
    {
        await _fixture.Store.DeleteAsync(DriveCredentialProvider.TokenKey(_onedrive), Token);

        var pro = (await _administration.ListAsync(Token)).Single(status => status.Name == "pro");

        Assert.False(pro.Ready);
        Assert.Equal("Drive account 'pro' needs an OAuth sign-in: run `orkeon drive login pro` in a terminal.", pro.Problem);
    }

    [Fact]
    public async Task Should_sign_in_with_a_device_code_and_store_the_tokens()
    {
        _fixture.Handler.Map(HttpMethod.Post, "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", FakeHttpResponse.Json(
            """{"device_code":"dev","user_code":"ABCD-EFGH","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}"""));
        _fixture.Handler.Map(HttpMethod.Post, "https://login.microsoftonline.com/consumers/oauth2/v2.0/token", FakeHttpResponse.Json(
            """{"access_token":"signed-in","refresh_token":"rt-new","expires_in":3600}"""));
        var interaction = new FakeOAuthLoginInteraction();

        await _administration.LoginAsync("pro", interaction, Token);

        Assert.Equal(("pro", "ABCD-EFGH"), (interaction.DeviceCode!.Value.Account, interaction.DeviceCode.Value.UserCode));
        Assert.Equal("signed-in", _fixture.Store.Tokens[DriveCredentialProvider.TokenKey(_onedrive)].AccessToken);
        var request = _fixture.Handler.RequestsTo(HttpMethod.Post, "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode").Single();
        Assert.Equal("https://graph.microsoft.com/Files.ReadWrite offline_access", request.Form["scope"]);
    }

    [Fact]
    public async Task Should_refuse_a_sign_in_without_a_refresh_token_and_translate_a_refused_one()
    {
        _fixture.Handler.Map(HttpMethod.Post, "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", FakeHttpResponse.Json(
            """{"device_code":"dev","user_code":"X","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}"""));
        _fixture.Handler.Enqueue(FakeHttpResponse.Json("""{"access_token":"short-lived","expires_in":3600}"""));
        _fixture.Handler.Enqueue(FakeHttpResponse.Json("""{"error":"access_denied","error_description":"The user declined."}"""));

        var noRefresh = await Assert.ThrowsAsync<DriveToolException>(() => _administration.LoginAsync("pro", new FakeOAuthLoginInteraction(), Token));
        var refused = await Assert.ThrowsAsync<DriveToolException>(() => _administration.LoginAsync("pro", new FakeOAuthLoginInteraction(), Token));

        Assert.Contains("offline_access", noRefresh.Message, StringComparison.Ordinal);
        Assert.Equal(DriveErrorCode.LoginRequired, refused.Code);
        Assert.Contains("access_denied", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_forget_the_tokens_on_logout()
    {
        Assert.True(await _administration.LogoutAsync("pro", Token));
        Assert.False(await _administration.LogoutAsync("pro", Token));
    }

    [Fact]
    public async Task Should_check_by_listing_the_root_folder()
    {
        _drives.DriveOf(_onedrive).AddFile("f1", "a.txt", "x");

        var check = await _administration.CheckAsync("pro", Token);

        Assert.Equal(("pro", "agents", "/Orkeon/agents", 1, false), (check.Account, check.RootName, check.RootPath, check.Items, check.More));
    }

    [Fact]
    public void Rights_check_should_name_the_tools_no_ready_account_grants()
    {
        var check = new DriveRightsCheck(_administration);
        DriveAccountStatus[] accounts =
        [
            new() { Name = "reader", Provider = "GoogleDrive", Rights = "Read", Ready = true },
            new() { Name = "sharer", Provider = "OneDrive", Rights = "Read, Share", Ready = true },
        ];

        var lines = check.Check(["drive_read", "drive_upload", "drive_share", "file_read"], accounts);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("drive_upload needs the Write right", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("drive_share needs the Share right with a non-empty Share:AllowedRecipients", lines[1], StringComparison.Ordinal);
    }
}
