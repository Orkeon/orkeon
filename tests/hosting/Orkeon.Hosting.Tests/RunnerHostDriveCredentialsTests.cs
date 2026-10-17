using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// DRIVE — the shared runner host keeps the OAuth tokens of drive accounts under the same
/// internal <c>/credentials</c> root as the e-mail family's, in a <c>drive</c> subdirectory of
/// their own: mounted as soon as a drive account is declared, owner-only on Unix, one directory
/// for both families. Serial for the same reasons as the e-mail tests.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostDriveCredentialsTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root;

    public RunnerHostDriveCredentialsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-drive-credentials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void A_drive_account_gets_the_credentials_root_its_own_subdirectory_and_the_file_token_store()
    {
        var tokens = Path.Combine(_root, "tokens");

        using var host = RunnerHost.Build(WriteSettings(drive: DriveSection(tokens)), new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(tokens, mount.BasePath);
        Assert.IsType<FileSystemDriveTokenStore>(host.Services.GetRequiredService<IDriveTokenStore>());
        Assert.True(Directory.Exists(Path.Combine(tokens, "drive")));
        Assert.False(Directory.Exists(Path.Combine(tokens, "email")));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(Path.Combine(tokens, "drive")));
        Assert.Contains("drive_upload", host.Services.GetServices<IBaseTool>().Select(tool => tool.Name));
    }

    [Fact]
    public void Both_families_share_one_credentials_directory_each_in_its_subdirectory()
    {
        var tokens = Path.Combine(_root, "tokens");

        using var host = RunnerHost.Build(WriteSettings(email: EmailSection(tokens), drive: DriveSection(credentialsDirectory: null)), new RunnerMountPlan());

        var mount = Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials);
        Assert.Equal(tokens, mount.BasePath);
        Assert.True(Directory.Exists(Path.Combine(tokens, "email")));
        Assert.True(Directory.Exists(Path.Combine(tokens, "drive")));
        Assert.IsType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
        Assert.IsType<FileSystemDriveTokenStore>(host.Services.GetRequiredService<IDriveTokenStore>());
    }

    [Fact]
    public void Two_different_credentials_directories_keep_the_e_mail_one_and_say_so()
    {
        var mail = Path.Combine(_root, "mail-tokens");
        var drive = Path.Combine(_root, "drive-tokens");
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(WriteSettings(email: EmailSection(mail), drive: DriveSection(drive)), new RunnerMountPlan());

            Assert.Equal(mail, Assert.Single(Mounts(host), m => m.VirtualPath == RunnerVirtualRoots.Credentials).BasePath);
            Assert.True(Directory.Exists(Path.Combine(mail, "drive")));
            Assert.False(Directory.Exists(drive));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("WARNING: Orkeon:Tools:Drive:CredentialsDirectory (" + drive + ") differs from Orkeon:Tools:Email:CredentialsDirectory", stderr.ToString(), StringComparison.Ordinal);
    }

    private static IReadOnlyList<FileSystemMount> Mounts(Microsoft.Extensions.Hosting.IHost host) =>
        host.Services.GetService<FileSystemRegistry>()?.GetMounts().ToList() ?? [];

    private static string DriveSection(string? credentialsDirectory) => $$"""
        {
          {{CredentialsEntry(credentialsDirectory)}}
          "Accounts": {
            "pro": { "Provider": "OneDrive", "Rights": "Read, Write", "Auth": { "ClientId": "client-id" } }
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
    private string WriteSettings(string? email = null, string? drive = null)
    {
        var sections = new List<string>();
        if (email is not null)
            sections.Add($"\"Email\": {email}");
        if (drive is not null)
            sections.Add($"\"Drive\": {drive}");
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Tools": { {{string.Join(", ", sections)}} } } }""");
        return path;
    }
}
