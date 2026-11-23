using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Configuration;

/// <summary>What a declared account becomes, and every problem it can have.</summary>
public sealed class ChatAccountResolverTests
{
    [Fact]
    public void Should_resolve_a_Teams_account()
    {
        var options = TestChatAccounts.Teams(read: ["team:Platform"], send: ["user:*@example.org"]);
        options.Send.MaxPerHour = 4;

        var account = TestChatAccounts.Resolve(options);

        Assert.Equal((ChatProvider.Teams, ChatCloud.Global, 4), (account.Provider, account.Cloud, account.MaxPerHour));
        Assert.Equal(PlaceForm.Team, Assert.Single(account.ReadPlaces).Form);
        Assert.Equal(PlaceForm.Domain, Assert.Single(account.SendPlaces).Form);
        Assert.Equal("select_account", account.OAuth.Prompt);
        Assert.True(account.OAuth.ScopesOnRefresh);
        Assert.Null(account.OAuth.ClientSecretEnvVar);
        Assert.Null(account.OAuth.RevocationEndpoint);
    }

    [Theory]
    [InlineData("Global", "graph.microsoft.com", "login.microsoftonline.com")]
    [InlineData("UsGov", "graph.microsoft.us", "login.microsoftonline.us")]
    [InlineData("UsGovDoD", "dod-graph.microsoft.us", "login.microsoftonline.us")]
    public void Should_use_the_hosts_of_the_cloud(string cloud, string api, string authority)
    {
        var options = TestChatAccounts.Teams();
        options.Cloud = Enum.Parse<ChatCloud>(cloud);

        var account = TestChatAccounts.Resolve(options);

        Assert.Equal(new Uri($"https://{api}/v1.0/"), account.ApiBase);
        Assert.Equal(new Uri($"https://{authority}/example.org/oauth2/v2.0/token"), account.OAuth.TokenEndpoint);
        Assert.Equal(new Uri($"https://{authority}/example.org/oauth2/v2.0/authorize"), account.OAuth.AuthorizationEndpoint);
        Assert.Equal(new Uri($"https://{authority}/example.org/oauth2/v2.0/devicecode"), account.OAuth.DeviceCodeEndpoint);
    }

    [Fact]
    public void Should_use_organizations_without_a_tenant()
    {
        var options = TestChatAccounts.Teams();
        options.Auth.Tenant = null;

        Assert.Contains("/organizations/", TestChatAccounts.Resolve(options).OAuth.TokenEndpoint.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_accept_the_six_rights_and_every_grant_in_any_case()
    {
        var account = TestChatAccounts.Resolve(TestChatAccounts.Teams(
            ChatRights.Read | ChatRights.Send | ChatRights.Edit | ChatRights.Delete | ChatRights.React | ChatRights.Meetings,
            granted: ["channelmessages", " ChannelEdit ", "Meetings", "CHANNELMEMBERS"]));

        Assert.All(Enum.GetValues<ChatRights>(), right => Assert.True(account.Grants(right)));
        Assert.All(Enum.GetValues<ChatAdminGrants>(), grant => Assert.True(account.IsGranted(grant)));
    }

    [Fact]
    public void Should_collect_every_problem_of_an_empty_account() => Assert.Equal(
        [
            "the account name may only hold letters, digits, '.', '_' and '-', starts with a letter or a digit, and has 64 characters at most",
            "Provider is required: Teams or TeamsWebhook",
            "Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Send\"",
            "Auth:ClientId is required (the id of your Microsoft Entra application)",
        ],
        ChatAccountResolver.Resolve("a b", new ChatAccountOptions()).Problems);

    [Theory]
    [InlineData("-a")]
    [InlineData(".a")]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("été")]
    public void Should_refuse_a_name_that_is_not_a_file_stem(string name) =>
        Assert.StartsWith("the account name", Assert.Single(ChatAccountResolver.Resolve(name, TestChatAccounts.Teams()).Problems), StringComparison.Ordinal);

    [Fact]
    public void Should_bound_the_length_of_a_name()
    {
        Assert.Empty(ChatAccountResolver.Resolve(new string('a', 64), TestChatAccounts.Teams()).Problems);
        Assert.Single(ChatAccountResolver.Resolve(new string('a', 65), TestChatAccounts.Teams()).Problems);
    }

    [Theory]
    [InlineData("webhook", "Webhook:UrlEnvVar goes with Provider TeamsWebhook alone")]
    [InlineData("certificate", "Auth:CertificatePathEnvVar goes with Auth:Method ClientCredentials alone")]
    [InlineData("federated", "Auth:FederatedTokenFileEnvVar goes with Auth:Method ClientCredentials alone")]
    [InlineData("secret", "Auth:ClientSecretEnvVar goes with Auth:Method ClientCredentials alone")]
    [InlineData("grant", "Auth:AdminGranted holds an entry that is not one of ChannelMessages, ChannelEdit, Meetings, ChannelMembers")]
    [InlineData("grant-none", "Auth:AdminGranted holds an entry that is not one of ChannelMessages, ChannelEdit, Meetings, ChannelMembers")]
    [InlineData("china", "Cloud China is not supported: Microsoft's pages give two different sign-in hosts for it")]
    [InlineData("client-variable", "Auth:ClientId or Auth:Tenant holds a variable reference, and Orkeon expands no variable here: write the id itself, it is not a secret")]
    [InlineData("tenant-variable", "Auth:ClientId or Auth:Tenant holds a variable reference, and Orkeon expands no variable here: write the id itself, it is not a secret")]
    [InlineData("tenant", "Auth:Tenant is not a tenant id, domain or alias")]
    [InlineData("tenant-path", "Auth:Tenant is not a tenant id, domain or alias")]
    [InlineData("consumers", "Auth:Tenant consumers is for personal accounts, which Microsoft Graph serves no Teams chat: write your organization's tenant id")]
    [InlineData("read-place", "Read:AllowedPlaces entry 'team:' is not a place: write team:<team>, team:<team>/channel:<channel>, chat:<id>, user:<address>, user:*@<domain> or *")]
    [InlineData("send-place", "Send:AllowedPlaces entry 'user:nobody' is not a place: write team:<team>, team:<team>/channel:<channel>, chat:<id>, user:<address>, user:*@<domain> or *")]
    [InlineData("cap", "Send:MaxPerHour must be positive")]
    public void Should_say_what_is_wrong(string wrong, string problem)
    {
        var options = TestChatAccounts.Teams();
        switch (wrong)
        {
            case "webhook": options.Webhook.UrlEnvVar = "HOOK"; break;
            case "certificate": options.Auth.CertificatePathEnvVar = "V"; break;
            case "federated": options.Auth.FederatedTokenFileEnvVar = "V"; break;
            case "secret": options.Auth.ClientSecretEnvVar = "V"; break;
            case "grant": options.Auth.AdminGranted.Add("Everything"); break;
            case "grant-none": options.Auth.AdminGranted.Add("None"); break;
            case "china": options.Cloud = ChatCloud.China; break;
            case "client-variable": options.Auth.ClientId = "${CLIENT}"; break;
            case "tenant-variable": options.Auth.Tenant = "${TENANT}"; break;
            case "tenant": options.Auth.Tenant = "a b"; break;
            case "tenant-path": options.Auth.Tenant = "a/../b"; break;
            case "consumers": options.Auth.Tenant = "Consumers"; break;
            case "read-place": options.Read.AllowedPlaces.Add("team:"); break;
            case "send-place": options.Send.AllowedPlaces.Add("user:nobody"); break;
            default: options.Send.MaxPerHour = 0; break;
        }

        var resolution = ChatAccountResolver.Resolve("work", options);

        Assert.Equal([problem], resolution.Problems);
        Assert.Null(resolution.Account);
    }

    [Fact]
    public void Should_resolve_a_webhook_whose_sign_in_and_cloud_are_not_read()
    {
        var options = TestChatAccounts.Webhook();
        options.Cloud = ChatCloud.China;
        options.Auth.Method = ChatAuthMethod.ClientCredentials;
        options.Auth.Tenant = "a b";
        options.Send.MaxPerHour = 3;

        var account = TestChatAccounts.Resolve(options);

        Assert.True(account.IsWebhook);
        Assert.Equal((ChatRights.Send, TestChatAccounts.WebhookVariable, 3), (account.Rights, account.WebhookUrlEnvVar, account.MaxPerHour));
        Assert.Equal("chat account 'work'", account.ToString());
        Assert.Throws<InvalidOperationException>(() => account.OAuth);
        Assert.Throws<InvalidOperationException>(() => account.ApiBase);
    }

    [Theory]
    [InlineData("read", "Rights must be Send, the one right a TeamsWebhook account has")]
    [InlineData("both", "Rights must be Send, the one right a TeamsWebhook account has")]
    [InlineData("missing", "Webhook:UrlEnvVar is required: the NAME of the environment variable holding the webhook's address, never the address itself")]
    [InlineData("https://secret.example.org/hook", "Webhook:UrlEnvVar is the NAME of an environment variable (letters, digits and '_'), never its value nor a reference to it")]
    [InlineData("${SECRET}", "Webhook:UrlEnvVar is the NAME of an environment variable (letters, digits and '_'), never its value nor a reference to it")]
    [InlineData("1SECRET", "Webhook:UrlEnvVar is the NAME of an environment variable (letters, digits and '_'), never its value nor a reference to it")]
    [InlineData("read-place", "AllowedPlaces is not read for a TeamsWebhook account, whose address fixes the one place: leave Read:AllowedPlaces and Send:AllowedPlaces out")]
    [InlineData("send-place", "AllowedPlaces is not read for a TeamsWebhook account, whose address fixes the one place: leave Read:AllowedPlaces and Send:AllowedPlaces out")]
    [InlineData("cap", "Send:MaxPerHour must be positive")]
    public void Should_say_what_is_wrong_in_a_webhook(string wrong, string problem)
    {
        var options = TestChatAccounts.Webhook();
        switch (wrong)
        {
            case "read": options.Rights = ChatRights.Read; break;
            case "both": options.Rights = ChatRights.Read | ChatRights.Send; break;
            case "missing": options.Webhook.UrlEnvVar = " "; break;
            case "read-place": options.Read.AllowedPlaces.Add("*"); break;
            case "send-place": options.Send.AllowedPlaces.Add("*"); break;
            case "cap": options.Send.MaxPerHour = 0; break;
            default: options.Webhook.UrlEnvVar = wrong; break;
        }

        var resolution = ChatAccountResolver.Resolve("work", options);

        Assert.Equal([problem], resolution.Problems);
        Assert.Null(resolution.Account);
    }
}
