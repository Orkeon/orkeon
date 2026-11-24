using Orkeon.Tools.Chat.Configuration;

namespace Orkeon.Tools.Chat.Tests.Fixtures;

/// <summary>Accounts as an operator declares them.</summary>
internal static class TestChatAccounts
{
    /// <summary>The Entra application of every account.</summary>
    public const string ClientId = "00000000-1111-2222-3333-444444444444";

    /// <summary>The tenant of every account.</summary>
    public const string Tenant = "example.org";

    /// <summary>A Teams account.</summary>
    public static ChatAccountOptions Teams(
        ChatRights rights = ChatRights.Read | ChatRights.Send, string[]? read = null, string[]? send = null, params string[] granted)
    {
        var options = new ChatAccountOptions { Provider = ChatProvider.Teams, Rights = rights };
        options.Auth.ClientId = ClientId;
        options.Auth.Tenant = Tenant;
        options.Auth.AdminGranted.AddRange(granted);
        options.Read.AllowedPlaces.AddRange(read ?? []);
        options.Send.AllowedPlaces.AddRange(send ?? []);
        return options;
    }

    /// <summary>The variable that holds a webhook's address.</summary>
    public const string WebhookVariable = "ORKEON_TEST_CHAT_HOOK";

    /// <summary>A webhook account.</summary>
    public static ChatAccountOptions Webhook(int? maxPerHour = null)
    {
        var options = new ChatAccountOptions { Provider = ChatProvider.TeamsWebhook, Rights = ChatRights.Send };
        options.Webhook.UrlEnvVar = WebhookVariable;
        options.Send.MaxPerHour = maxPerHour;
        return options;
    }

    /// <summary>The variable that holds, or leads to, an application's credential.</summary>
    public const string ApplicationVariable = "ORKEON_TEST_CHAT_APP";

    /// <summary>An application account, signed in with a client secret.</summary>
    public static ChatAccountOptions Application(params string[] read)
    {
        var options = Teams(ChatRights.Read, read);
        options.Auth.Method = ChatAuthMethod.ClientCredentials;
        options.Auth.ClientSecretEnvVar = ApplicationVariable;
        return options;
    }

    /// <summary><paramref name="options"/> resolved.</summary>
    public static ResolvedChatAccount Resolve(ChatAccountOptions options, string name = "work")
    {
        var resolution = ChatAccountResolver.Resolve(name, options);
        Assert.Empty(resolution.Problems);
        Assert.NotNull(resolution.Account);
        return resolution.Account;
    }
}
