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
        Assert.True(Directory.Exists(tokens));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(tokens));
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

        var error = Assert.Throws<InvalidOperationException>(() => RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(elsewhere)}:{RunnerVirtualRoots.Credentials}:rw"] }));

        Assert.StartsWith("The virtual root /credentials is reserved", error.Message, StringComparison.Ordinal);
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
