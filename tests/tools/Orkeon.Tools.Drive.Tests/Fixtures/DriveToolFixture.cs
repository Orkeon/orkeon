using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Administration;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Security;
using Orkeon.Tools.Drive.Tests.Doubles;
using Orkeon.Tools.Drive.Tools;

namespace Orkeon.Tools.Drive.Tests.Fixtures;

/// <summary>
/// The eleven drive tools over a fake drive per account, a fake VFS (<c>/workspace</c> read-only,
/// <c>/output</c> writable) and a fresh token for every account.
/// </summary>
internal sealed class DriveToolFixture : IDisposable
{
    private readonly Dictionary<string, ToolBase> _tools = new(StringComparer.Ordinal);

    /// <summary>Creates the fixture: accounts <c>full</c> (every right, default), <c>reader</c>, <c>personal</c> (a personal OneDrive).</summary>
    public DriveToolFixture(bool withholdRejected = false)
    {
        Options.Screening.WithholdRejected = withholdRejected;
        Options.DefaultAccount = "full";
        var full = TestDriveAccounts.Google(AllRights);
        full.Share.AllowedRecipients.AddRange(["*@example.com", "boss@corp.test"]);
        full.MaxUploadMegabytes = 1;
        Options.Accounts["full"] = full;
        Options.Accounts["reader"] = TestDriveAccounts.Google(DriveRights.Read);
        Options.Accounts["personal"] = TestDriveAccounts.OneDrive(AllRights);

        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var registry = new DriveAccountRegistry(options);
        var access = new DriveAccess(registry, Drives);
        var screen = new DriveContentScreen(options);
        Http = new HttpClient(Handler, disposeHandler: false);
        var oauth = new OAuth2Client(Http, TimeProvider.System);
        var credentials = new DriveCredentialProvider(Tokens, oauth, TestDriveAccounts.SecretEnvironment());
        foreach (var name in registry.Names)
        {
            Tokens.With(DriveCredentialProvider.TokenKey(registry.Resolve(name)), new DriveTokenSet
            {
                AccessToken = "token",
                RefreshToken = "refresh",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            });
        }

        Administration = new DriveAccountAdministration(registry, credentials, oauth, Drives);
        Registry = registry;

        Add(new DriveAccountsTool(Administration));
        Add(new DriveListTool(access));
        Add(new DriveSearchTool(access));
        Add(new DriveInfoTool(access));
        Add(new DriveReadTool(access, screen));
        Add(new DriveDownloadTool(access, Files));
        Add(new DriveUploadTool(access, Files));
        Add(new DriveCreateFolderTool(access));
        Add(new DriveMoveTool(access));
        Add(new DriveDeleteTool(access));
        Add(new DriveShareTool(access));
    }

    /// <summary>Every right.</summary>
    public const DriveRights AllRights = DriveRights.Read | DriveRights.Write | DriveRights.Organize | DriveRights.Share | DriveRights.Delete | DriveRights.Purge;

    /// <summary>The bound options, editable before a call.</summary>
    public DriveToolsOptions Options { get; } = new();

    /// <summary>The account registry.</summary>
    public DriveAccountRegistry Registry { get; }

    /// <summary>The fake drives.</summary>
    public FakeDriveFactory Drives { get; } = new();

    /// <summary>The token store.</summary>
    public FakeDriveTokenStore Tokens { get; } = new();

    /// <summary>The VFS.</summary>
    public FakeRightsFileSystemService Files { get; } =
        new FakeRightsFileSystemService().AddMount("/workspace", FileAccessRights.ReadOnly).AddMount("/output", FileAccessRights.ReadWrite);

    /// <summary>The administration behind <c>drive_accounts</c>.</summary>
    public DriveAccountAdministration Administration { get; }

    /// <summary>The tools by name.</summary>
    public IReadOnlyDictionary<string, ToolBase> Tools => _tools;

    private FakeHttpMessageHandler Handler { get; } = new();

    private HttpClient Http { get; }

    /// <summary>The fake drive of <paramref name="account"/>.</summary>
    public FakeDrive Drive(string account = "full") => Drives.DriveOf(Registry.Resolve(account));

    /// <summary>Calls <paramref name="tool"/> through the tool protocol.</summary>
    public Task<ToolCallResponse> CallAsync(string tool, params (string Key, object? Value)[] parameters) =>
        _tools[tool].CallAsync(ToolResults.Call(tool, parameters), TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var tool in _tools.Values)
            tool.Dispose();
        Http.Dispose();
        Handler.Dispose();
    }

    private void Add(ToolBase tool) => _tools.Add(tool.Name, tool);
}
