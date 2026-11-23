using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Chat.Configuration;

/// <summary>
/// What a host needs to know, before its services exist, to provide the OAuth token store:
/// whether any chat account is declared (every one signs in with OAuth2), and where the
/// operator wants the tokens.
/// </summary>
public static class ChatCredentialsLocation
{
    /// <summary>The virtual directory, under the host's credentials root, the chat tokens live in.</summary>
    public const string TokenSubdirectory = "chat";

    /// <summary>Whether the <c>Orkeon:Tools:Chat</c> section declares an account.</summary>
    /// <param name="chatSection">The <c>Orkeon:Tools:Chat</c> section.</param>
    public static bool NeedsTokenStore(IConfiguration chatSection)
    {
        ArgumentNullException.ThrowIfNull(chatSection);
        return Bind(chatSection).Accounts.Count > 0;
    }

    /// <summary>The operator's <c>CredentialsDirectory</c>, or null to use the host's default.</summary>
    /// <param name="chatSection">The <c>Orkeon:Tools:Chat</c> section.</param>
    public static string? ConfiguredDirectory(IConfiguration chatSection)
    {
        ArgumentNullException.ThrowIfNull(chatSection);
        var directory = Bind(chatSection).CredentialsDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
    }

    // Never throws: a value the binder cannot convert leaves its account out, reported when used.
    private static ChatToolsOptions Bind(IConfiguration chatSection)
    {
        var options = new ChatToolsOptions();
        ChatOptionsBinder.Bind(chatSection, options);
        return options;
    }
}
