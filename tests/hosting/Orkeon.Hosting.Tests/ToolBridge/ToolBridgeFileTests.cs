using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.FileSystem;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the files of a bridged call: a path stays a path of the caller, what the tool writes on the bridge is written
/// on the caller, a file an argument names is sent, both under a size limit; the scratch is gone after the call.
/// </summary>
public sealed class ToolBridgeFileTests : IDisposable
{
    private readonly BridgeFileSystem _bridgeFiles = new();
    private readonly FakeFileSystemService _callerFiles = new FakeFileSystemService()
        .AddMount("/output", FileAccessRights.ReadWrite)
        .AddMount("/input", FileAccessRights.ReadOnly)
        .AddFile("/input/page.txt", "from the caller");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _bridgeFiles.Dispose();

    private static ToolCallRequest Call(string name, object? value) =>
        new("firefox", new Dictionary<string, object?> { ["action"] = "read", [name] = value });

    private async Task<(ToolBridgeServer Server, ToolBridgeLink Link, IBaseTool Bridged)> BridgeAsync(
        FakeFileBridgeTool remote, long? callerMax = null, long? bridgeMax = null)
    {
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"], files: _bridgeFiles,
            configure: o => o.MaxFileBytes = bridgeMax ?? o.MaxFileBytes);
        var registry = new ToolRegistry([new FakeFileBridgeTool("firefox", _callerFiles)]);
        var options = BridgeLoopback.ClientOptions(address, "firefox");
        if (callerMax is { } max)
            options.MaxFileBytes = max;
        var link = await BridgeLoopback.ConnectAsync(registry, options, fileSystem: _callerFiles);
        return (server, link, (await registry.GetToolByNameAsync("firefox", Ct))!);
    }

    [Fact]
    public async Task ShouldWriteTheFileOnTheCaller_WhenTheToolWroteItOnTheBridge()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service) { Written = [7, 8, 9] };
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        var response = await bridged.CallAsync(Call("save_to", "/output/shot.png"), Ct);

        // Assert
        Assert.True(response.Success, response.Error);
        Assert.Equal([7, 8, 9], await _callerFiles.TryReadAllBytesAsync("/output/shot.png", Ct));
    }

    [Fact]
    public async Task ShouldSendTheFile_WhenAnArgumentNamesAFileTheCallerCanRead()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service);
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        await bridged.CallAsync(Call("read_from", "/input/page.txt"), Ct);

        // Assert
        Assert.Equal("from the caller", Assert.Single(remote.Read));
    }

    [Fact]
    public async Task ShouldSendNothingBack_WhenTheToolOnlyReadTheFileItWasSent()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service);
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        var response = await bridged.CallAsync(Call("read_from", "/input/page.txt"), Ct);

        // Assert
        Assert.True(response.Success, response.Error);
    }

    [Fact]
    public async Task ShouldRemoveTheScratchFolder_WhenTheCallEnded()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service);
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        await bridged.CallAsync(Call("save_to", "/output/shot.png"), Ct);

        // Assert
        Assert.False(File.Exists(Assert.Single(remote.WrittenAt)));
    }

    [Fact]
    public async Task ShouldRefuseTheCallBeforeSendingIt_WhenTheFilesItNamesExceedTheCallerLimit()
    {
        // Arrange
        _callerFiles.AddFile("/input/big.bin", new byte[64]);
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service);
        var (server, link, bridged) = await BridgeAsync(remote, callerMax: 32);
        await using var _ = server;
        using var __ = link;

        // Act
        var response = await bridged.CallAsync(Call("read_from", "/input/big.bin"), Ct);

        // Assert
        Assert.Equal((false, 0), (response.Success, remote.Calls.Count));
        Assert.Contains("MaxFileBytes", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailTheCall_WhenTheFilesTheToolWroteExceedTheBridgeLimit()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service) { Written = new byte[64] };
        var (server, link, bridged) = await BridgeAsync(remote, bridgeMax: 32);
        await using var _ = server;
        using var __ = link;

        // Act
        var response = await bridged.CallAsync(Call("save_to", "/output/shot.png"), Ct);

        // Assert
        Assert.Equal((false, false), (response.Success, await _callerFiles.ExistsAsync("/output/shot.png", Ct)));
        Assert.Contains("Orkeon:Bridge:MaxFileBytes", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheWrite_WhenThePathIsInAReadOnlyMountOfTheCaller()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service);
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        var response = await bridged.CallAsync(Call("save_to", "/input/shot.png"), Ct);

        // Assert
        Assert.Equal((false, false), (response.Success, await _callerFiles.ExistsAsync("/input/shot.png", Ct)));
    }

    [Fact]
    public async Task ShouldLeaveTheResultAsTheToolGaveIt_WhenTheFileCameBack()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service) { Response = new ToolCallResponse(true, "saved", null) };
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        var response = await bridged.CallAsync(Call("save_to", "/output/shot.png"), Ct);

        // Assert
        Assert.Equal("saved", response.Result);
    }

    [Fact]
    public async Task ShouldCarryTheBytesUnchanged_WhenTheFileIsText()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service) { Written = Encoding.UTF8.GetBytes("é\n") };
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        await bridged.CallAsync(Call("save_to", "/output/note.txt"), Ct);

        // Assert
        Assert.Equal("é\n", await _callerFiles.TryReadAllTextAsync("/output/note.txt", Ct));
    }

    [Fact]
    public async Task ShouldNotAskTheFileSystem_WhenAStringArgumentIsUnderNoMount()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service);
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        await bridged.CallAsync(Call("read_from", "/elsewhere/page.txt"), Ct);

        // Assert
        Assert.DoesNotContain("read", _callerFiles.Validated);
        Assert.DoesNotContain("/elsewhere/page.txt", _callerFiles.Validated);
    }

    [Fact]
    public void ShouldAllowTheScratchFolder_WhenTheServingHostIsComposed()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        // Act
        ToolBridgeStartup.RegisterServing(services);

        // Assert
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Orkeon.Infrastructure.Configuration.PathSecurityOptions>>(provider).Value;
        Assert.Contains(ToolBridgeScratch.Parent, options.AdditionalAllowedDirectories);
    }

    [Fact]
    public async Task ShouldMakeTheScratchUnderTheAllowedFolder_WhenACallCarriesTheCallersMounts()
    {
        // Arrange
        var remote = new FakeFileBridgeTool("firefox", _bridgeFiles.Service) { Written = [1] };
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        await bridged.CallAsync(Call("save_to", "/output/shot.png"), Ct);

        // Assert
        Assert.StartsWith(ToolBridgeScratch.Parent + Path.DirectorySeparatorChar, Assert.Single(remote.WrittenAt), StringComparison.Ordinal);
    }
}
