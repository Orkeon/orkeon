using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// #482 — the REPL reads the OAuth tokens <c>orkeon chat|email|drive login</c> kept, through the internal
/// <c>/credentials</c> root, as a runner does: mounted when a declared account signs in with OAuth, closed
/// to the agent-facing file system, and a warning at start, never a refusal, when it cannot be provided.
/// <para>
/// Serial, in the collection of <see cref="ReplSettingsSourcesTests"/>: the global settings path and
/// stderr are process-wide state.
/// </para>
/// </summary>
[Collection(ReplWorkingDirectoryCollection.Name)]
public sealed class ReplTokenStoresTests : IDisposable
{
    private const string ChatAccounts = """{ "work": { "Provider": "Teams", "Rights": "Read", "Auth": { "Method": "OAuth2", "ClientId": "c" } } }""";

    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-repl-tokens-").FullName;
    private readonly string? _globalOverride = RunnerSettings.GlobalSettingsPathOverride;
    private readonly List<string> _reported = [];

    public ReplTokenStoresTests() =>
        RunnerSettings.GlobalSettingsPathOverride = Path.Combine(_root, "global", "appsettings.json");

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = _globalOverride;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReadTheTokensOfASignIn_WhenAChatAccountSignsInAsAUser()
    {
        var tokens = Path.Combine(_root, "tokens");
        using var host = Start(Settings("settings", Chat(ChatAccounts, tokens)));
        var store = host.Services.GetRequiredService<IChatTokenStore>();
        var written = new ChatTokenSet { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTimeOffset.UnixEpoch };

        Assert.IsType<FileSystemChatTokenStore>(store);
        await store.WriteAsync("work-0123456789ab", written, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(tokens, "chat", "work-0123456789ab.json")));
        Assert.Equal("a", (await store.ReadAsync("work-0123456789ab", TestContext.Current.CancellationToken))?.AccessToken);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHideTheTokensFromTheAgents_WhenAMountHoldsTheirFolder()
    {
        var tokens = Path.Combine(_root, "workspace", "tokens");
        using var host = Start(Settings("settings", Chat(ChatAccounts, tokens)));
        await File.WriteAllTextAsync(Path.Combine(tokens, "chat", "work.json"), "{}", TestContext.Current.CancellationToken);
        var agents = host.Services.GetRequiredService<IFileSystemService>();

        await Assert.ThrowsAsync<FileAccessDeniedException>(() => agents.TryReadAllTextAsync($"{RunnerVirtualRoots.Credentials}/chat/work.json", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileAccessDeniedException>(() => agents.TryReadAllTextAsync("/workspace/tokens/chat/work.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ShouldKeepTheTokensOfTheThreeFamilies_InOneDirectory()
    {
        var tokens = Path.Combine(_root, "tokens");
        var oauth = (string provider) => $$"""{ "me": { "Provider": "{{provider}}", "Address": "a@b.c", "Rights": "Read", "Auth": { "Method": "OAuth2", "ClientId": "c" } } }""";
        using var host = Start(Settings("settings",
            Family("Email", oauth("Gmail"), tokens), Family("Drive", oauth("GoogleDrive"), null), Chat(ChatAccounts, null)));

        Assert.IsType<FileSystemEmailTokenStore>(host.Services.GetRequiredService<IEmailTokenStore>());
        Assert.IsType<FileSystemDriveTokenStore>(host.Services.GetRequiredService<IDriveTokenStore>());
        Assert.IsType<FileSystemChatTokenStore>(host.Services.GetRequiredService<IChatTokenStore>());
        foreach (var family in new[] { "email", "drive", "chat" })
            Assert.True(Directory.Exists(Path.Combine(tokens, family)), family);
    }

    [Fact]
    public void ShouldMountNothing_WhenNoAccountSignsIn()
    {
        const string webhook = """{ "ops": { "Provider": "TeamsWebhook", "Rights": "Send", "Webhook": { "UrlEnvVar": "HOOK" } } }""";
        using var host = Start(Settings("settings", Chat(webhook, null)));

        Assert.Null(CredentialsMount(host.Services));
        Assert.IsNotType<FileSystemChatTokenStore>(host.Services.GetRequiredService<IChatTokenStore>());
        Assert.False(Directory.Exists(Path.Combine(_root, "global", "credentials")));
    }

    [Fact]
    public void ShouldUseTheFolderBesideThePerUserSettings_WhenNoneIsNamed()
    {
        using var host = Start(Settings("settings", Chat(ChatAccounts, null)));

        Assert.Equal(Path.Combine(_root, "global", "credentials"), CredentialsMount(host.Services)?.BasePath);
    }

    [Fact]
    public void ShouldReadARelativeDirectoryFromTheFileThatDeclaresIt_WhenSeveralSettingsFilesAreGiven()
    {
        var first = Settings("first", Chat(ChatAccounts, "tokens"));
        var second = Settings("second");

        using var host = Start(first, second);

        Assert.Equal(Path.Combine(_root, "first", "tokens"), CredentialsMount(host.Services)?.BasePath);
    }

    [Fact]
    public void ShouldWarnAndStart_WhenTheDirectoryCannotBePrepared()
    {
        var file = Path.Combine(_root, "a-file");
        File.WriteAllText(file, "");

        var (host, stderr) = StartCapturingStderr(Settings("settings", Chat(ChatAccounts, file)));
        using var _ = host;

        Assert.Contains("OAuth chat accounts have no token store in this run", stderr, StringComparison.Ordinal);
        Assert.Null(CredentialsMount(host.Services));
        Assert.IsNotType<FileSystemChatTokenStore>(host.Services.GetRequiredService<IChatTokenStore>());
    }

    [Fact]
    public void ShouldWarnAndStart_WhenTheFolderOfACredentialFileIsMissing()
    {
        var variable = "ORKEON_TEST_REPL_FILE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, Path.Combine(_root, "absent", "app.pem"));
        try
        {
            var application = $$"""{ "app": { "Provider": "Teams", "Rights": "Read", "Auth": { "Method": "ClientCredentials", "CertificatePathEnvVar": "{{variable}}" } } }""";

            var (host, stderr) = StartCapturingStderr(Settings("settings", Chat(application, null)));
            using var _ = host;

            Assert.Contains("The folder of a credential file", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static FileSystemMount? CredentialsMount(IServiceProvider services) =>
        services.GetRequiredService<FileSystemRegistry>().GetMounts().SingleOrDefault(m => m.VirtualPath == RunnerVirtualRoots.Credentials);

    private static string Chat(string accounts, string? directory) => Family("Chat", accounts, directory);

    private static string Family(string name, string accounts, string? directory) =>
        $$"""{{JsonSerializer.Serialize(name)}}: { "Accounts": {{accounts}}{{(directory is null ? "" : $", \"CredentialsDirectory\": {JsonSerializer.Serialize(directory)}")}} }""";

    private string Settings(string folder, params string[] families)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, folder)).FullName, "appsettings.json");
        File.WriteAllText(path, $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Tools": { {{string.Join(", ", families)}} } } }""");
        return path;
    }

    private Microsoft.Extensions.Hosting.IHost Start(params string[] settings)
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "workspace")).FullName;
        string[] args = [.. settings.SelectMany(file => new[] { "--settings", file }), "--mount", $"{workspace}:/workspace:rw", "--allow-external-mounts"];
        var launch = ReplStartup.Prepare(args, UiMode.Plain, replWordWrap: false, _reported.Add);
        Assert.True(launch.Host is not null, string.Join(Environment.NewLine, _reported));
        return launch.Host;
    }

    private (Microsoft.Extensions.Hosting.IHost Host, string Stderr) StartCapturingStderr(string settings)
    {
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            var host = Start(settings);
            return (host, stderr.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
