using Jint;
using Jint.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Drive.DependencyInjection;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The drive family as a script reaches it — the real tools behind <c>tools.drive*</c> — held to
/// the keys <c>Typings/tools.d.ts</c> declares for them. Every drive call needs an account and a
/// network; the key checks run before any account is resolved, so a family with no account
/// configured is enough for them, and the answer a call without an account gets is the one the
/// typings promise a script: a failure that says what to configure.
/// </summary>
public sealed class DriveToolTypingsTests
{
    [Fact]
    public async Task Every_drive_tool_is_reachable_under_its_declared_camel_case_member()
    {
        await using var services = BuildDriveTools();

        var reachable = (await EvaluateAsync(services, """
            Promise.resolve(['driveAccounts', 'driveList', 'driveSearch', 'driveInfo', 'driveRead', 'driveDownload',
                'driveUpload', 'driveCreateFolder', 'driveMove', 'driveDelete', 'driveShare']
                .every(name => typeof tools[name] === 'function'))
            """)).AsBoolean();

        Assert.True(reachable);
    }

    [Fact]
    public async Task A_required_argument_is_found_by_its_snake_case_name_only()
    {
        await using var services = BuildDriveTools();

        var camel = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync(services, "tools.driveDownload({ id: 'f1', targetDirectory: '/output' })"));
        var snake = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync(services, "tools.driveDownload({ id: 'f1', directory: '/output' })"));

        Assert.Contains("Required parameter 'directory' is missing", camel.ToString(), StringComparison.Ordinal);
        Assert.Contains("No drive account is configured", snake.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_accounts_of_an_unconfigured_family_are_an_empty_list()
    {
        await using var services = BuildDriveTools();

        var count = (await EvaluateAsync(services, "tools.driveAccounts().then(r => r.accounts.length)")).AsNumber();

        Assert.Equal(0d, count);
    }

    private static ServiceProvider BuildDriveTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite));
        services.AddOrkeonDriveTools(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static Task<JsValue> EvaluateAsync(ServiceProvider services, string script)
    {
        var engine = new JsEngineFactory(builtInTools: services.GetServices<IBaseTool>().ToArray()).Create();
        return Task.Run(() => engine.Evaluate(script).UnwrapIfPromise());
    }
}
