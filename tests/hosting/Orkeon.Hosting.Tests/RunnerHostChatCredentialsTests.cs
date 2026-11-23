using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #446 — the shared runner host keeps the OAuth tokens of chat accounts under the same
/// internal <c>/credentials</c> root as the e-mail family's, in a <c>chat</c> subdirectory of
/// their own: mounted as soon as a chat account is declared, owner-only on Unix, one directory
/// for every family. Serial for the same reasons as the e-mail tests.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostChatCredentialsTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root;

    public RunnerHostChatCredentialsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-chat-credentials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void A_chat_account_gets_the_credentials_root_its_own_subdirectory_and_the_file_token_store()
    {
        var tokens = Path.Combine(_root, "tokens");

        using var host = RunnerHost.Build(WriteSettings(chat: ChatSection(tokens)), new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(tokens, mount.BasePath);
        Assert.IsType<FileSystemChatTokenStore>(host.Services.GetRequiredService<IChatTokenStore>());
        Assert.True(Directory.Exists(Path.Combine(tokens, "chat")));
        Assert.False(Directory.Exists(Path.Combine(tokens, "email")));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(Path.Combine(tokens, "chat")));
        Assert.Contains("chat_send", host.Services.GetServices<IBaseTool>().Select(tool => tool.Name));
    }

    [Fact]
    public void A_webhook_alone_needs_no_credentials_root()
    {
        const string webhook = """{ "Accounts": { "ops": { "Provider": "TeamsWebhook", "Rights": "Send", "Webhook": { "UrlEnvVar": "HOOK" } } } }""";

        using var host = RunnerHost.Build(WriteSettings(chat: webhook), new RunnerMountPlan());

        Assert.DoesNotContain(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
    }

    [Fact]
    public void An_application_alone_needs_no_credentials_root()
    {
        const string application = """{ "Accounts": { "ops": { "Provider": "Teams", "Rights": "Read", "Auth": { "Method": "ClientCredentials", "ClientSecretEnvVar": "S" } } } }""";

        using var host = RunnerHost.Build(WriteSettings(chat: application), new RunnerMountPlan());

        Assert.DoesNotContain(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
    }

    [Fact]
    public void Both_families_share_one_credentials_directory_each_in_its_subdirectory()
    {
        var tokens = Path.Combine(_root, "tokens");

        using var host = RunnerHost.Build(WriteSettings(email: EmailSection(tokens), chat: ChatSection(credentialsDirectory: null)), new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(tokens, mount.BasePath);
        Assert.True(Directory.Exists(Path.Combine(tokens, "email")));
        Assert.True(Directory.Exists(Path.Combine(tokens, "chat")));
        Assert.IsType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
        Assert.IsType<FileSystemChatTokenStore>(host.Services.GetRequiredService<IChatTokenStore>());
    }

    [Fact]
    public void Two_different_credentials_directories_keep_the_e_mail_one_and_say_so()
    {
        var mail = Path.Combine(_root, "mail-tokens");
        var chat = Path.Combine(_root, "chat-tokens");
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(email: EmailSection(mail), chat: ChatSection(chat)), new RunnerMountPlan());

            Assert.Equal(mail, Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials).BasePath);
            Assert.True(Directory.Exists(Path.Combine(mail, "chat")));
            Assert.False(Directory.Exists(chat));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("WARNING: Orkeon:Tools:Chat:CredentialsDirectory (" + chat + ") differs from Orkeon:Tools:Email:CredentialsDirectory", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_chat_subdirectory_that_cannot_be_prepared_leaves_the_e_mail_token_store_standing()
    {
        var tokens = Path.Combine(_root, "tokens");
        Directory.CreateDirectory(tokens);
        // A file where the chat subdirectory should be: it cannot be created.
        File.WriteAllText(Path.Combine(tokens, "chat"), "not a directory");
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(email: EmailSection(tokens), chat: ChatSection(credentialsDirectory: null)), new RunnerMountPlan());

            Assert.Equal(tokens, Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials).BasePath);
            Assert.IsType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("WARNING: The directory of the chat OAuth tokens", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("The directory of the e-mail OAuth tokens", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_an_e_mail_account_the_drive_directory_wins_and_the_chat_one_is_said_to_be_ignored()
    {
        var drive = Path.Combine(_root, "drive-tokens");
        var chat = Path.Combine(_root, "chat-tokens");
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(chat: ChatSection(chat), drive: DriveSection(drive)), new RunnerMountPlan());

            Assert.Equal(drive, Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials).BasePath);
            Assert.True(Directory.Exists(Path.Combine(drive, "drive")));
            Assert.True(Directory.Exists(Path.Combine(drive, "chat")));
            Assert.False(Directory.Exists(chat));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains(
            $"WARNING: Orkeon:Tools:Chat:CredentialsDirectory ({chat}) differs from Orkeon:Tools:Drive:CredentialsDirectory ({drive}): the runner keeps one credentials directory, the drive one, and the chat tokens go there too.",
            stderr.ToString(), StringComparison.Ordinal);
    }

    private static IReadOnlyList<FileSystemMount> Mounts(Microsoft.Extensions.Hosting.IHost host) =>
        host.Services.GetService<FileSystemRegistry>()?.GetMounts().ToList() ?? [];

    private static string ChatSection(string? credentialsDirectory) => $$"""
        {
          {{CredentialsEntry(credentialsDirectory)}}
          "Accounts": {
            "pro": { "Provider": "Teams", "Rights": "Read, Send", "Auth": { "ClientId": "client-id" } }
          }
        }
        """;

    private static string DriveSection(string credentialsDirectory) => $$"""
        {
          {{CredentialsEntry(credentialsDirectory)}}
          "Accounts": {
            "pro": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "client-id" } }
          }
        }
        """;

    private static string EmailSection(string credentialsDirectory) => $$"""
        {
          {{CredentialsEntry(credentialsDirectory)}}
          "Accounts": {
            "hotmail": { "Provider": "Outlook", "Address": "me@outlook.com", "Rights": "Read", "Auth": { "ClientId": "client-id" } }
          }
        }
        """;

    private static string CredentialsEntry(string? credentialsDirectory) =>
        credentialsDirectory is null ? string.Empty : $"\"CredentialsDirectory\": {JsonSerializer.Serialize(credentialsDirectory)},";

    /// <summary>A settings file with these tool sections, RaggableTree off so no model loads.</summary>
    private string WriteSettings(string? email = null, string? drive = null, string? chat = null)
    {
        var sections = new List<string>();
        if (email is not null)
            sections.Add($"\"Email\": {email}");
        if (drive is not null)
            sections.Add($"\"Drive\": {drive}");
        if (chat is not null)
            sections.Add($"\"Chat\": {chat}");
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Tools": { {{string.Join(", ", sections)}} } } }""");
        return path;
    }
}
