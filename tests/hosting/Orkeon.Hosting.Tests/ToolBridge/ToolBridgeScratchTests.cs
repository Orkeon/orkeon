using System.Globalization;
using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the folder the bridge makes its scratches in, the files a scratch sends back, and the physical paths of a
/// scratch scrubbed from a tool's answer.
/// </summary>
public sealed class ToolBridgeScratchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BridgeCallRequest Call(params BridgeFile[] files) => new()
    {
        CallId = "c1",
        Tool = "firefox",
        Arguments = JsonDocument.Parse("{}").RootElement.Clone(),
        Mounts = [new BridgeMount { Path = "/output", Writable = true }],
        Files = [.. files],
    };

    [Fact]
    public void ShouldNameTheParentUnpredictably_WhenItIsCreated()
    {
        // Arrange
        var predictable = "orkeon-bridge-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        // Act
        var parent = ToolBridgeScratch.Parent;

        // Assert
        Assert.NotEqual(predictable, Path.GetFileName(parent));
    }

    [Fact]
    public void ShouldKeepTheParentToItsOwner_WhenItIsCreated()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no Unix mode; its temporary folder is the user's own.");

        // Act
        var mode = OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(ToolBridgeScratch.Parent);

        // Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, mode);
    }

    [Fact]
    public async Task ShouldNeitherReturnNorCountASentFile_WhenTheToolLeftItUnchanged()
    {
        // Arrange
        using var files = new BridgeFileSystem();
        using var scratch = ToolBridgeScratch.Enter(Call(new BridgeFile { Path = "/output/big.bin", Content = new byte[100] }), files.Scope, files.Boot);
        var folder = scratch.Folders.Single().Physical;
        await File.WriteAllBytesAsync(Path.Combine(folder, "new.bin"), [1, 2, 3], Ct);

        // Act
        var written = await scratch.WrittenAsync(maxBytes: 10, Ct);

        // Assert
        Assert.Equal("/output/new.bin", Assert.Single(written).Path);
    }

    [Fact]
    public async Task ShouldReturnASentFile_WhenTheToolChangedIt()
    {
        // Arrange
        using var files = new BridgeFileSystem();
        using var scratch = ToolBridgeScratch.Enter(Call(new BridgeFile { Path = "/output/a.bin", Content = new byte[] { 1 } }), files.Scope, files.Boot);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Folders.Single().Physical, "a.bin"), [2], Ct);

        // Act
        var written = await scratch.WrittenAsync(maxBytes: 10, Ct);

        // Assert
        Assert.Equal(new byte[] { 2 }, Assert.Single(written).Content.ToArray());
    }

    [Fact]
    public async Task ShouldRefuseTheFiles_WhenAFileTheToolWroteExceedsTheBound()
    {
        // Arrange
        using var files = new BridgeFileSystem();
        using var scratch = ToolBridgeScratch.Enter(Call(), files.Scope, files.Boot);
        await File.WriteAllBytesAsync(Path.Combine(scratch.Folders.Single().Physical, "big.bin"), new byte[11], Ct);

        // Act
        var refusal = await Assert.ThrowsAsync<ToolBridgeFileException>(() => scratch.WrittenAsync(maxBytes: 10, Ct));

        // Assert
        Assert.Contains("MaxFileBytes", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldPutTheCallersVirtualPathInPlaceOfTheScratchFolder_WhenAnAnswerNamesIt()
    {
        // Arrange
        using var files = new BridgeFileSystem();
        using var scratch = ToolBridgeScratch.Enter(Call(), files.Scope, files.Boot);
        var folder = scratch.Folders.Single().Physical;
        using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new { path = folder + "/report.txt" }));
        var answer = new BridgeCallResponse
        {
            Success = false,
            Error = $"{folder}/report.txt is locked",
            Metadata = new Dictionary<string, JsonElement> { ["file"] = metadata.RootElement.Clone() }.ToImmutableDictionary(),
        };

        // Act
        var scrubbed = ToolBridgeRedaction.Scrub(answer, ToolBridgeRedaction.PathsOf(files.Boot, scratch));

        // Assert
        Assert.Equal(("/output/report.txt is locked", "/output/report.txt"),
            (scrubbed.Error, scrubbed.Metadata!["file"].GetProperty("path").GetString()));
    }
}
