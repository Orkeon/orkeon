using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Chat.DependencyInjection;
using Orkeon.Tools.Drive.DependencyInjection;
using Orkeon.Tools.Email.DependencyInjection;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The forge's tool policy: what escapes the VFS never enters a forged crew. The mailbox tools
/// are offered (FORGE-10: a try runs them on a trial mailbox); the shell, the
/// interpreter, the drive tools and the chat tools are denied.
/// </summary>
public sealed class ForgeSandboxTests
{
    private static readonly string[] Others = ["file_read", "shell_command", "code_interpreter"];
    private static readonly string[] Denied =
    [
        "chat_accounts", "chat_delete", "chat_edit", "chat_list", "chat_members", "chat_react", "chat_read", "chat_search", "chat_send",
        "code_interpreter",
        "drive_accounts", "drive_create_folder", "drive_delete", "drive_download", "drive_info", "drive_list",
        "drive_move", "drive_read", "drive_search", "drive_share", "drive_upload",
        "shell_command",
    ];

    [Fact]
    [Trait("BR", "BR-01")]
    public void ShouldOfferTheTwelveMailTools_WhenTheHostRegistersThem()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonEmailTools(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var family = provider.GetServices<IBaseTool>().ToList();
        Assert.Equal(13, family.Count);
        var registered = family
            .Concat(Others.Select(name => new StubBaseTool(name)))
            .ToList();

        var catalogue = ForgeSandbox.SelectCrewTools(registered).Select(tool => tool.Name).ToList();

        Assert.Equal(family.Select(tool => tool.Name).Append("file_read").Order(StringComparer.Ordinal), catalogue);
        Assert.DoesNotContain("shell_command", catalogue);
        Assert.DoesNotContain("code_interpreter", catalogue);
    }

    [Fact]
    [Trait("BR", "BR-10")]
    public void ShouldDenyShellCodeInterpreterTheDriveToolsAndTheChatTools_Only()
    {
        Assert.Equal(Denied, ForgeSandbox.DeniedTools.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ShouldLeaveEveryDriveToolOut_WhenTheHostRegistersThem()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonDriveTools(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registered = provider.GetServices<IBaseTool>().Append(new StubBaseTool("file_read")).ToList();

        var catalogue = ForgeSandbox.SelectCrewTools(registered).Select(tool => tool.Name).ToList();

        Assert.Equal(["file_read"], catalogue);
    }

    [Fact]
    public void ShouldLeaveEveryChatToolOut_WhenTheHostRegistersThem()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonChatTools(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registered = provider.GetServices<IBaseTool>().Append(new StubBaseTool("file_read")).ToList();

        var catalogue = ForgeSandbox.SelectCrewTools(registered).Select(tool => tool.Name).ToList();

        Assert.Equal(["file_read"], catalogue);
    }
}
