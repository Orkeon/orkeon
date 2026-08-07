using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// <see cref="CodeToolExtensions.AddOrkeonCodeTools"/>: the allowlist config keys must
/// actually shape the registered <see cref="ShellCommandTool"/> — asserted through real
/// <c>CallAsync</c> validation results, never through internals.
/// </summary>
public sealed class CodeToolExtensionsTests
{
    private static IBaseTool ResolveShellTool(
        Dictionary<string, string?>? config, bool registerConfiguration = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        if (registerConfiguration)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(config ?? [])
                .Build());
        }

        services.AddOrkeonCodeTools();
        var tool = services.BuildServiceProvider().GetRequiredService<IBaseTool>();
        Assert.Equal("shell_command", tool.Name);
        return tool;
    }

    private static async Task<ToolCallResponse> CallAsync(IBaseTool tool, string command)
        => await tool.CallAsync(new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?> { ["command"] = command }),
            TestContext.Current.CancellationToken);

    private static void AssertNotAllowlistRejected(ToolCallResponse result)
    {
        if (!result.Success)
            Assert.DoesNotContain("not in the allowlist", result.Error ?? "");
    }

    [Fact]
    public async Task ShouldAllowExtraCommand_WhenExtraAllowedCommandsConfigured()
    {
        var tool = ResolveShellTool(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:ExtraAllowedCommands:0"] = "make",
        });

        // 'make' may not exist on the machine (start failure is fine) — what must
        // NOT happen is the allowlist rejection.
        AssertNotAllowlistRejected(await CallAsync(tool, "make --version"));
    }

    [Fact]
    public async Task ShouldKeepDefaultAllowlist_WhenExtraAllowedCommandsConfigured()
    {
        var tool = ResolveShellTool(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:ExtraAllowedCommands:0"] = "make",
        });

        var result = await CallAsync(tool, "echo hello");

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ShouldKeepInterpreterOptIn_WhenExtraAllowedCommandsConfigured()
    {
        // Extras are ADDITIVE: they must not cancel AllowInterpreters (which a full
        // replacement list does, per the ctor contract) nor re-enable the git
        // read-only subcommand restriction.
        var tool = ResolveShellTool(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:AllowInterpreters"] = "true",
            ["Orkeon:Tools:Shell:ExtraAllowedCommands:0"] = "make",
        });

        AssertNotAllowlistRejected(await CallAsync(tool, "dotnet --info"));

        var gitResult = await CallAsync(tool, "git stash list");
        if (!gitResult.Success)
            Assert.DoesNotContain("read-only subcommands", gitResult.Error ?? "");
    }

    [Fact]
    public async Task ShouldReplaceAllowlist_WhenAllowedCommandsConfigured()
    {
        var tool = ResolveShellTool(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:AllowedCommands:0"] = "python3",
        });

        var result = await CallAsync(tool, "echo hello");

        Assert.False(result.Success);
        Assert.Contains("not in the allowlist", result.Error ?? "");
    }

    [Fact]
    public async Task ShouldCancelInterpreterOptIn_WhenAllowedCommandsConfigured()
    {
        // A replacement list cancels AllowInterpreters (existing ctor contract), so
        // the git read-only subcommand restriction comes back.
        var tool = ResolveShellTool(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:AllowInterpreters"] = "true",
            ["Orkeon:Tools:Shell:AllowedCommands:0"] = "git",
            ["Orkeon:Tools:Shell:AllowedCommands:1"] = "echo",
        });

        var result = await CallAsync(tool, "git stash list");

        Assert.False(result.Success);
        Assert.Contains("read-only subcommands", result.Error ?? "");
    }

    [Fact]
    public async Task ShouldUseDefaultAllowlist_WhenNoShellSectionConfigured()
    {
        var tool = ResolveShellTool(config: null);

        var echo = await CallAsync(tool, "echo hello");
        Assert.True(echo.Success, echo.Error);

        var build = await CallAsync(tool, "dotnet build App.sln");
        Assert.False(build.Success);
        Assert.Contains("not in the allowlist", build.Error ?? "");
    }

    [Fact]
    public async Task ShouldUseDefaultAllowlist_WhenAllowedCommandsSectionIsEmpty()
    {
        // The trap: an empty-but-non-null list would block EVERYTHING. An empty or
        // valueless section must map to null (defaults), never to an empty array.
        var tool = ResolveShellTool(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Shell:AllowedCommands"] = "",
        });

        var result = await CallAsync(tool, "echo hello");

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ShouldUseDefaultAllowlist_WhenNoConfigurationRegistered()
    {
        var tool = ResolveShellTool(config: null, registerConfiguration: false);

        var result = await CallAsync(tool, "echo hello");

        Assert.True(result.Success, result.Error);
    }
}
