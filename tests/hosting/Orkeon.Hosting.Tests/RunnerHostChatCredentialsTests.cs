using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tools.Chat.Administration;
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
    private readonly string _variable = "ORKEON_TEST_CHAT_FILE_" + Guid.NewGuid().ToString("N");

    public RunnerHostChatCredentialsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-chat-credentials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
        Environment.SetEnvironmentVariable(_variable, null);
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

    [Fact]
    public async Task The_folder_of_an_application_credential_file_is_an_internal_read_only_mount()
    {
        var file = CredentialFile("keys");

        using var host = RunnerHost.Build(WriteSettings(chat: ApplicationSection()), new RunnerMountPlan());

        var mount = Assert.Single(CredentialFileMounts(host));
        Assert.Equal(Path.GetDirectoryName(file), mount.BasePath);
        Assert.Equal((MountVisibility.Internal, FileAccessRights.ReadOnly), (mount.Visibility, mount.DefaultRights));
        var privileged = host.Services.GetRequiredService<PrivilegedFileSystemAccess>().FileSystem;
        Assert.Equal("jwt.a.b", await privileged.TryReadAllTextAsync(mount.VirtualPath + "/token", TestContext.Current.CancellationToken));
        Assert.False(privileged.ResolveAndValidate(mount.VirtualPath + "/token", FileAccessRights.Write).IsAllowed);
        var status = Assert.Single(await host.Services.GetRequiredService<ChatAccountAdministration>().ListAsync(TestContext.Current.CancellationToken));
        Assert.Null(status.CredentialProblem);
    }

    [Fact]
    public void No_agent_reaches_a_credential_file_by_its_root_nor_by_a_mount_that_holds_its_folder()
    {
        var file = CredentialFile(Path.Combine("data", "keys"));
        File.WriteAllText(Path.Combine(_root, "data", "seen.txt"), "seen");
        var plan = new RunnerMountPlan { CliMounts = [$"{Path.Combine(_root, "data")}:/data:ro"], AllowExternalMounts = true };

        using var host = RunnerHost.Build(WriteSettings(chat: ApplicationSection()), plan);

        var mount = Assert.Single(CredentialFileMounts(host));
        var agents = host.Services.GetRequiredService<IFileSystemService>();
        Assert.True(agents.ResolveAndValidate("/data/seen.txt", FileAccessRights.Read).IsAllowed);
        Assert.False(agents.ResolveAndValidate(mount.VirtualPath + "/token", FileAccessRights.Read).IsAllowed);
        Assert.False(agents.ResolveAndValidate("/data/keys/token", FileAccessRights.Read).IsAllowed);
        Assert.Null(agents.ToVirtualPath(file));
        Assert.DoesNotContain(agents.GetAvailableMounts(), m => m.VirtualPath.StartsWith(RunnerVirtualRoots.CredentialFiles, StringComparison.Ordinal));
    }

    [Fact]
    public void A_credential_file_in_a_folder_mounted_for_the_agents_is_not_mounted_and_said()
    {
        CredentialFile("data");
        var plan = new RunnerMountPlan { CliMounts = [$"{Path.Combine(_root, "data")}:/data:ro"], AllowExternalMounts = true };
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(chat: ApplicationSection()), plan);

            Assert.Empty(CredentialFileMounts(host));
            Assert.True(host.Services.GetRequiredService<IFileSystemService>().ResolveAndValidate("/data/token", FileAccessRights.Read).IsAllowed);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("Keep it in a folder of its own.", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_root, stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosts_own_credential_file_is_mounted_without_any_chat_account_and_closed_to_the_agents()
    {
        var file = CredentialFile("keys");
        var plan = new RunnerMountPlan { CredentialFiles = [(file, "Orkeon:Host:Teams")] };

        using var host = RunnerHost.Build(WriteSettings(chat: "{}"), plan);

        var mount = Assert.Single(CredentialFileMounts(host));
        var privileged = host.Services.GetRequiredService<PrivilegedFileSystemAccess>().FileSystem;
        Assert.Equal("jwt.a.b", await privileged.TryReadAllTextAsync(mount.VirtualPath + "/token", TestContext.Current.CancellationToken));
        Assert.False(privileged.ResolveAndValidate(mount.VirtualPath + "/token", FileAccessRights.Write).IsAllowed);
        Assert.False(host.Services.GetRequiredService<IFileSystemService>().ResolveAndValidate(mount.VirtualPath + "/token", FileAccessRights.Read).IsAllowed);
    }

    [Fact]
    public void A_hosts_own_credential_file_beside_what_the_agents_see_is_not_mounted_and_its_key_is_said()
    {
        var file = CredentialFile("data");
        var plan = new RunnerMountPlan
        {
            CliMounts = [$"{Path.Combine(_root, "data")}:/data:ro"],
            AllowExternalMounts = true,
            CredentialFiles = [(file, "Orkeon:Host:Teams")],
        };
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(chat: "{}"), plan);

            Assert.Empty(CredentialFileMounts(host));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("Orkeon:Host:Teams", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_root, stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_a_chat_account_and_the_host_share_is_mounted_once()
    {
        var file = CredentialFile("keys");
        var plan = new RunnerMountPlan { CredentialFiles = [(file, "Orkeon:Host:Teams")] };

        using var host = RunnerHost.Build(WriteSettings(chat: ApplicationSection()), plan);

        Assert.Single(CredentialFileMounts(host));
    }

    [Theory]
    [InlineData("/credential-files")]
    [InlineData("/credential-files/mine")]
    public void A_user_mount_on_the_credential_files_root_is_refused(string root)
    {
        var plan = new RunnerMountPlan { CliMounts = [$"{_root}:{root}:ro"], AllowExternalMounts = true };

        var refused = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(WriteSettings(chat: ApplicationSection()), plan).Dispose());

        Assert.Contains("The virtual root /credential-files is reserved", refused.Message, StringComparison.Ordinal);
    }

    private static List<FileSystemMount> CredentialFileMounts(Microsoft.Extensions.Hosting.IHost host) =>
        [.. Mounts(host).Where(m => m.VirtualPath.StartsWith(RunnerVirtualRoots.CredentialFiles + "/", StringComparison.Ordinal))];

    /// <summary>A federated token file in <paramref name="folder"/> of the test's root, named by the test's variable.</summary>
    private string CredentialFile(string folder)
    {
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, folder)).FullName, "token");
        File.WriteAllText(file, "jwt.a.b");
        Environment.SetEnvironmentVariable(_variable, file);
        return file;
    }

    private string ApplicationSection() => $$"""
        {
          "Accounts": {
            "ops": {
              "Provider": "Teams", "Rights": "Read",
              "Auth": { "Method": "ClientCredentials", "ClientId": "client-id", "Tenant": "contoso.example", "FederatedTokenFileEnvVar": "{{_variable}}" },
              "Read": { "AllowedPlaces": [ "chat:19:weekly@thread.v2" ] }
            }
          }
        }
        """;

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
