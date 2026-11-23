namespace Orkeon.Tools.Chat.Constants;

/// <summary>
/// Vendor endpoints, scopes and limits of the chat providers, as learn.microsoft.com documents
/// them (read 2026-11-23), and the sizes of the tools' answers.
/// </summary>
internal static class ChatDefaults
{
    /// <summary>The configuration section the family binds, relative to the root.</summary>
    public const string SectionName = Orkeon.Constants.Configuration.ConfigurationKeys.ToolsChat;

    /// <summary>Named HTTP client used by the providers and the OAuth2 client; it follows no redirect.</summary>
    public const string HttpClientName = "orkeon.chat";

    /// <summary>The guide every configuration problem points at.</summary>
    public const string GuidePath = "docs/guides/chat.md";

    /// <summary>The tenant alias of any work or school account, when the account names no tenant.</summary>
    public const string DefaultTenant = "organizations";

    /// <summary>Asks the Microsoft identity platform for a refresh token.</summary>
    public const string OfflineAccessScope = "offline_access";

    /// <summary>The sign-in prompt: the account is chosen, and consent is asked only when it is missing.</summary>
    public const string SignInPrompt = "select_account";

    /// <summary>Where a Microsoft user or administrator removes an application's access.</summary>
    public const string RemoveAccessHint =
        "Microsoft publishes no token revocation endpoint: a tenant administrator removes the application's access in the Microsoft Entra admin center (Enterprise applications, the application, Permissions)";

    /// <summary>The most messages one page of Graph holds.</summary>
    public const int MaxPage = 50;

    /// <summary>The page size when a call does not say.</summary>
    public const int DefaultPage = 20;

    /// <summary>How many Graph pages one <c>chat_read</c> goes through to find messages inside its dates.</summary>
    public const int MaxPagesPerRead = 5;

    /// <summary>How many pages of the user's chats are searched for the one-to-one chat with a person.</summary>
    public const int MaxChatPages = 10;

    /// <summary>How many paging links one listing of channels or members follows.</summary>
    public const int MaxListPages = 20;

    /// <summary>How many message authors one page looks up in the directory.</summary>
    public const int MaxAuthorLookups = 20;

    /// <summary>The most mentions one message carries.</summary>
    public const int MaxMentions = 20;

    /// <summary>The largest message sent, in bytes of HTML: Microsoft recommends 80 KB of the 100 KB Teams accepts.</summary>
    public const int MaxMessageBytes = 80 * 1024;

    /// <summary>The characters of each message's text one page shows when the call does not say.</summary>
    public const int DefaultTextChars = 1000;

    /// <summary>The most characters of a message's text one page shows.</summary>
    public const int MaxTextChars = 3000;

    /// <summary>The longest name (person, team, channel, topic, attachment) a result shows.</summary>
    public const int MaxLabelChars = 120;

    /// <summary>How long a call waits for a throttled provider before it answers <c>Throttled</c>.</summary>
    public static readonly TimeSpan MaxThrottleWait = TimeSpan.FromSeconds(5);

    /// <summary>The pause taken after a 429 that names none.</summary>
    public static readonly TimeSpan DefaultThrottleWait = TimeSpan.FromSeconds(10);

    /// <summary>How long a post waits for its turn in a place before it answers <c>Throttled</c>.</summary>
    public static readonly TimeSpan MaxPostWait = TimeSpan.FromSeconds(30);

    /// <summary>Graph takes one post a second per channel and per chat.</summary>
    public static readonly TimeSpan PostInterval = TimeSpan.FromSeconds(1);
}
