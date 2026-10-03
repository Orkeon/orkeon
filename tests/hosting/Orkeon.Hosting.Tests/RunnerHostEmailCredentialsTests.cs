using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// MAIL — the shared runner host keeps the OAuth tokens of e-mail accounts under the internal
/// <c>/credentials</c> root: mounted only when an OAuth account is declared, created owner-only
/// on Unix, reserved against a user mount, and never brought down by an e-mail section it
/// cannot even read. Serial: one test moves the process-global per-user settings path, and
/// every host build without an <c>Llm</c> section warns on stderr.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostEmailCredentialsTests : IDisposable
{
    private readonly string _root;

    public RunnerHostEmailCredentialsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-email-credentials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void An_OAuth_account_gets_the_credentials_root_and_the_file_token_store()
    {
        var tokens = Path.Combine(_root, "tokens");

        using var host = RunnerHost.Build(WriteSettings(OAuthAccount(tokens)), new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(tokens, mount.BasePath);
        Assert.IsType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
        Assert.True(Directory.Exists(Path.Combine(tokens, "email")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(tokens));
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(Path.Combine(tokens, "email")));
        }
    }

    [Fact]
    public void The_directory_holding_the_tokens_is_narrowed_to_its_owner_when_it_already_existed_wider()
    {
        if (OperatingSystem.IsWindows())
            return;

        var tokens = Path.Combine(_root, "tokens");
        var email = Path.Combine(tokens, "email");
        Directory.CreateDirectory(email);
        File.SetUnixFileMode(email, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        using var host = RunnerHost.Build(WriteSettings(OAuthAccount(tokens)), new RunnerMountPlan());

        Assert.Equal(OwnerOnly, File.GetUnixFileMode(email));
    }

    [Fact]
    public void Without_a_CredentialsDirectory_the_tokens_sit_next_to_the_per_user_settings()
    {
        var globalSettings = Path.Combine(_root, "config", "Orkeon", "appsettings.json");
        RunnerSettings.GlobalSettingsPathOverride = globalSettings;

        using var host = RunnerHost.Build(WriteSettings(OAuthAccount(credentialsDirectory: null)), new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(Path.Combine(_root, "config", "Orkeon", "credentials"), mount.BasePath);
    }

    [Fact]
    public void A_relative_CredentialsDirectory_is_read_from_the_settings_file_s_directory()
    {
        var settings = WriteSettings(OAuthAccount("tokens"));

        using var host = RunnerHost.Build(settings, new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(Path.Combine(_root, "tokens"), mount.BasePath);
    }

    [Fact]
    public void A_token_directory_that_cannot_be_created_leaves_the_run_standing_and_says_why()
    {
        if (OperatingSystem.IsWindows())
            return;

        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var tokens = Path.Combine(locked, "tokens");
        try
        {
            Directory.CreateDirectory(tokens);
            return; // Permissions are not enforced for this user (root): nothing to prove here.
        }
        catch (UnauthorizedAccessException)
        {
            // Expected: the runner meets the same refusal below.
        }

        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(OAuthAccount(tokens)), new RunnerMountPlan());

            Assert.DoesNotContain(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
            Assert.IsNotType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
        }
        finally
        {
            Console.SetError(original);
            File.SetUnixFileMode(locked, OwnerOnly);
        }

        Assert.Contains("WARNING: The directory of the e-mail OAuth tokens, " + tokens + ", cannot be prepared", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Password_accounts_mount_nothing_and_create_nothing()
    {
        var tokens = Path.Combine(_root, "tokens");

        using var host = RunnerHost.Build(WriteSettings(PasswordAccount(tokens)), new RunnerMountPlan());

        Assert.DoesNotContain(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.False(Directory.Exists(tokens));
        Assert.IsNotType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
    }

    [Fact]
    public void A_user_mount_on_the_credentials_root_is_refused_once_an_OAuth_account_is_declared()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        var settings = WriteSettings(OAuthAccount(Path.Combine(_root, "tokens")));

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(elsewhere)}:{RunnerVirtualRoots.Credentials}:rw"] }));

        Assert.StartsWith("The virtual root /credentials is reserved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_mount_on_the_credentials_root_is_refused_even_without_an_OAuth_account()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        var settings = WriteSettings(PasswordAccount(Path.Combine(_root, "tokens")));

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(elsewhere)}:{RunnerVirtualRoots.Credentials}:rw"] }));

        Assert.StartsWith("The virtual root /credentials is reserved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_command_s_early_guard_refuses_the_credentials_root_by_name()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        bool free;
        try
        {
            free = RunnerExecution.EnsureReservedRootsAreFree(
                [$"{FileSystemMount.Quote(elsewhere)}:{RunnerVirtualRoots.Credentials}:rw"], settingsPath: null, RunnerVirtualRoots.Crew);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.False(free);
        Assert.Contains("'/credentials' is a virtual root reserved by the runner", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("reserved here: /crew, /credentials", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_e_mail_section_the_binder_cannot_convert_leaves_the_host_and_every_tool_standing()
    {
        var crew = Path.Combine(_root, "crew");
        Directory.CreateDirectory(crew);
        var settings = WriteSettings("""
            {
              "Screening": { "WithholdRejected": "maybe" },
              "Accounts": {
                "hotmail": { "Provider": "Outlook", "Address": "me@outlook.com", "Rights": "Read, Organise" },
                "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Port": "nine" } }
              }
            }
            """);

        using var host = RunnerHost.Build(
            settings,
            new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(crew)}:{RunnerVirtualRoots.Crew}:ro"] });

        var tools = host.Services.GetServices<IBaseTool>().Select(tool => tool.Name).ToList();
        Assert.Contains("email_send", tools);
        Assert.Contains("file_read", tools);
    }

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static IReadOnlyList<FileSystemMount> Mounts(Microsoft.Extensions.Hosting.IHost host) =>
        host.Services.GetService<FileSystemRegistry>()?.GetMounts().ToList() ?? [];

    private static string OAuthAccount(string? credentialsDirectory) => $$"""
        {
          {{CredentialsEntry(credentialsDirectory)}}
          "Accounts": {
            "hotmail": { "Provider": "Outlook", "Address": "me@outlook.com", "Rights": "Read", "Auth": { "ClientId": "client-id" } }
          }
        }
        """;

    private static string PasswordAccount(string credentialsDirectory) => $$"""
        {
          {{CredentialsEntry(credentialsDirectory)}}
          "Accounts": {
            "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "SOME_PASSWORD" } }
          }
        }
        """;

    private static string CredentialsEntry(string? credentialsDirectory) =>
        credentialsDirectory is null ? string.Empty : $"\"CredentialsDirectory\": {JsonSerializer.Serialize(credentialsDirectory)},";

    /// <summary>A settings file with this e-mail section, RaggableTree off so no model loads.</summary>
    private string WriteSettings(string emailSection)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Tools": { "Email": {{emailSection}} } } }""");
        return path;
    }
}
