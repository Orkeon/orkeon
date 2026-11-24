using Microsoft.Extensions.Configuration;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Auth;

namespace Orkeon.Tools.Chat.Configuration;

/// <summary>
/// What a host needs to know, before its services exist, to provide the OAuth token store —
/// whether a chat account signs in as a user, and where the operator wants the tokens — and to
/// mount the credential files of the accounts that sign in as an application.
/// </summary>
public static class ChatCredentialsLocation
{
    /// <summary>The virtual directory, under the host's credentials root, the chat tokens live in.</summary>
    public const string TokenSubdirectory = "chat";

    /// <summary>Whether the <c>Orkeon:Tools:Chat</c> section declares an account that signs in, or one that cannot be read.</summary>
    /// <param name="chatSection">The <c>Orkeon:Tools:Chat</c> section.</param>
    public static bool NeedsTokenStore(IConfiguration chatSection)
    {
        ArgumentNullException.ThrowIfNull(chatSection);
        return Bind(chatSection).Accounts.Values.Any(account => account.Provider != ChatProvider.TeamsWebhook && account.Auth.Method != ChatAuthMethod.ClientCredentials);
    }

    /// <summary>The operator's <c>CredentialsDirectory</c>, or null to use the host's default.</summary>
    /// <param name="chatSection">The <c>Orkeon:Tools:Chat</c> section.</param>
    public static string? ConfiguredDirectory(IConfiguration chatSection)
    {
        ArgumentNullException.ThrowIfNull(chatSection);
        var directory = Bind(chatSection).CredentialsDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
    }

    /// <summary>
    /// The directories holding the certificate and federated token files the application accounts name, each
    /// with the folder, under the host's root for them, it is mounted at. A variable that is unset, or that
    /// names no absolute path of a file, is left out: its account says so when it is used.
    /// </summary>
    /// <param name="chatSection">The <c>Orkeon:Tools:Chat</c> section.</param>
    public static IReadOnlyDictionary<string, string> CredentialFileDirectories(IConfiguration chatSection)
    {
        ArgumentNullException.ThrowIfNull(chatSection);
        var directories = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var auth in Bind(chatSection).Accounts.Values.Select(account => account.Auth))
        {
            foreach (var variable in new[] { auth.CertificatePathEnvVar, auth.FederatedTokenFileEnvVar })
            {
                if (string.IsNullOrWhiteSpace(variable) || SecretEnvironment.Machine.Read(variable.Trim()) is not { } value)
                    continue;
                var path = value.Trim();
                if (ChatCredentialFile.Directory(path) is { } directory && ChatCredentialFile.Folder(path) is { } folder)
                    directories[directory] = folder;
            }
        }

        return directories;
    }

    // Never throws: a value the binder cannot convert leaves its account out, reported when used.
    private static ChatToolsOptions Bind(IConfiguration chatSection)
    {
        var options = new ChatToolsOptions();
        ChatOptionsBinder.Bind(chatSection, options);
        return options;
    }
}
