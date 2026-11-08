using System.Text;
using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-18: <c>/output</c> is served as read-only MCP resources (<c>orkeon:///output/&lt;path&gt;</c>)
/// through <see cref="IFileSystemService"/> and its rights, behind the opt-in
/// <see cref="McpServerOptions.ExposeOutput"/>. <c>/workspace</c>, any other mount and any physical
/// path are never exposed.
/// </summary>
public sealed class McpServerResourcesTests
{
    private const int NotFound = -32002;

    private static FakeRightsFileSystemService Files() =>
        new FakeRightsFileSystemService()
            .AddMount("/output", FileAccessRights.ReadOnly)
            .AddMount("/workspace", FileAccessRights.Read | FileAccessRights.Write)
            .AddFile("/output/report.md", Encoding.UTF8.GetBytes("# report"))
            .AddFile("/output/sub/data.csv", Encoding.UTF8.GetBytes("a,b"))
            .AddFile("/workspace/secret.txt", Encoding.UTF8.GetBytes("top secret"));

    private static McpServer Server(IFileSystemService fs, McpServerOptions options) =>
        new(new MockToolRegistry(), ToolInvocationPipeline.Unguarded, fs, options);

    private static async Task<JsonRpcResponse> SendAsync(McpServer server, string method, string? paramsJson = null)
    {
        var request = new JsonRpcRequest
        {
            Method = method,
            Id = JsonSerializer.SerializeToElement(1),
            Params = paramsJson is null ? null : JsonElement.Parse(paramsJson),
        };
        return (await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken))!;
    }

    private static JsonElement ResultOf(JsonRpcResponse response)
    {
        Assert.Null(response.Error);
        return response.Result!.Value;
    }

    private static List<string> Uris(JsonElement list) =>
        list.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("uri").GetString()!).ToList();

    [Fact]
    public async Task ShouldListTheFilesOfOutputOnly_WhenOutputIsExposed()
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });

        var list = ResultOf(await SendAsync(server, "resources/list"));

        Assert.Equal(["orkeon:///output/report.md", "orkeon:///output/sub/data.csv"], Uris(list));
        var first = list.GetProperty("resources")[0];
        Assert.Equal("report.md", first.GetProperty("name").GetString());
        Assert.Equal("text/markdown", first.GetProperty("mimeType").GetString());
        Assert.DoesNotContain("workspace", list.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAdvertiseTheResourcesCapability_WhenOutputIsExposed()
    {
        var exposed = Server(Files(), new McpServerOptions { ExposeOutput = true });
        var hidden = Server(Files(), new McpServerOptions());

        var withResources = ResultOf(await SendAsync(exposed, "initialize", """{"protocolVersion":"2025-11-25"}"""));
        var without = ResultOf(await SendAsync(hidden, "initialize", """{"protocolVersion":"2025-11-25"}"""));

        var capabilities = withResources.GetProperty("capabilities");
        Assert.True(capabilities.TryGetProperty("resources", out var resources));
        Assert.False(resources.TryGetProperty("subscribe", out _));
        Assert.False(without.GetProperty("capabilities").TryGetProperty("resources", out _));
    }

    [Fact]
    public async Task ShouldReadAFileOfOutputAsText_WhenTheUriNamesIt()
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });

        var read = ResultOf(await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/sub/data.csv"}"""));

        var content = Assert.Single(read.GetProperty("contents").EnumerateArray());
        Assert.Equal("orkeon:///output/sub/data.csv", content.GetProperty("uri").GetString());
        Assert.Equal("a,b", content.GetProperty("text").GetString());
        Assert.Equal("text/csv", content.GetProperty("mimeType").GetString());
    }

    [Fact]
    public async Task ShouldReadABinaryFileAsABlob_WhenItIsNotText()
    {
        var bytes = new byte[] { 0xFF, 0xFE, 0x00, 0x01 };
        var fs = Files().AddFile("/output/img.bin", bytes);
        var server = Server(fs, new McpServerOptions { ExposeOutput = true });

        var read = ResultOf(await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/img.bin"}"""));

        var content = read.GetProperty("contents")[0];
        Assert.False(content.TryGetProperty("text", out _));
        Assert.Equal(Convert.ToBase64String(bytes), content.GetProperty("blob").GetString());
    }

    [Theory]
    [InlineData("orkeon:///workspace/secret.txt")]
    [InlineData("orkeon:///output/../workspace/secret.txt")]
    [InlineData("orkeon:///output/%2e%2e/workspace/secret.txt")]
    [InlineData("orkeon:///output/sub/../../workspace/secret.txt")]
    [InlineData("file:///workspace/secret.txt")]
    [InlineData("orkeon:///outputs/report.md")]
    [InlineData("orkeon:///output")]
    public async Task ShouldRefuseTheRead_WhenTheUriLeavesOutput(string uri)
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });

        var response = await SendAsync(server, "resources/read", JsonSerializer.Serialize(new { uri }));

        Assert.NotNull(response.Error);
        Assert.Null(response.Result);
        Assert.DoesNotContain("top secret", JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAnswerResourceNotFound_WhenTheFileIsMissing()
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });

        var response = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/nope.txt"}""");

        Assert.Equal(NotFound, response.Error!.Code);
    }

    [Fact]
    public async Task ShouldRefuseTheRead_WhenTheOutputMountGrantsNoRead()
    {
        var fs = new FakeRightsFileSystemService()
            .AddMount("/output", FileAccessRights.Write | FileAccessRights.Create)
            .AddFile("/output/report.md", Encoding.UTF8.GetBytes("# report"));
        var server = Server(fs, new McpServerOptions { ExposeOutput = true });

        var read = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/report.md"}""");
        var list = await SendAsync(server, "resources/list");

        Assert.NotNull(read.Error);
        Assert.DoesNotContain("# report", JsonSerializer.Serialize(read), StringComparison.Ordinal);
        Assert.NotNull(list.Error);
        Assert.Contains(fs.Validations, v => v.Right == FileAccessRights.Read);
    }

    [Theory]
    [InlineData("resources/list")]
    [InlineData("resources/read")]
    public async Task ShouldAnswerMethodNotFound_WhenOutputIsNotExposed(string method)
    {
        var server = Server(Files(), new McpServerOptions());

        var response = await SendAsync(server, method, """{"uri":"orkeon:///output/report.md"}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Fact]
    public async Task ShouldPageTheList_WhenThereAreMoreFilesThanAPage()
    {
        var fs = new FakeRightsFileSystemService().AddMount("/output", FileAccessRights.ReadOnly);
        for (var i = 1; i <= 5; i++)
            fs.AddFile($"/output/f{i}.txt", Encoding.UTF8.GetBytes("x"));
        var server = Server(fs, new McpServerOptions { ExposeOutput = true, ResourcesPageSize = 2 });

        var pages = new List<List<string>>();
        string? cursor = null;
        do
        {
            var paramsJson = cursor is null ? "{}" : JsonSerializer.Serialize(new { cursor });
            var page = ResultOf(await SendAsync(server, "resources/list", paramsJson));
            pages.Add(Uris(page));
            cursor = page.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        }
        while (cursor is not null && pages.Count < 10);

        Assert.Equal([2, 2, 1], pages.Select(p => p.Count));
        Assert.Equal(
            Enumerable.Range(1, 5).Select(i => $"orkeon:///output/f{i}.txt"),
            pages.SelectMany(p => p));
    }

    [Fact]
    public async Task ShouldAnswerInvalidParams_WhenTheCursorIsNotOneTheServerIssued()
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });

        var response = await SendAsync(server, "resources/list", """{"cursor":"not-a-cursor"}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
    }

    [Fact]
    public async Task ShouldRefuseTheRead_WhenTheFileIsLargerThanTheCap()
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true, MaxResourceBytes = 4 });

        var response = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/report.md"}""");

        Assert.NotNull(response.Error);
        Assert.DoesNotContain("# report", JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRevealNoPhysicalPath_WhenTheMountIsBackedByADisk()
    {
        var root = Directory.CreateTempSubdirectory("orkeon-fwk18-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "a.txt"), "disk", TestContext.Current.CancellationToken);
            var fs = new DiskBackedFileSystemService(root.FullName, "/output");
            var server = Server(fs, new McpServerOptions { ExposeOutput = true });

            var list = await SendAsync(server, "resources/list");
            var read = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/a.txt"}""");
            var missing = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/none.txt"}""");
            var escape = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/../a.txt"}""");

            Assert.Equal(["orkeon:///output/a.txt"], Uris(ResultOf(list)));
            Assert.Equal("disk", ResultOf(read).GetProperty("contents")[0].GetProperty("text").GetString());
            foreach (var response in new[] { list, read, missing, escape })
                Assert.DoesNotContain(root.FullName, JsonSerializer.Serialize(response), StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("orkeon:///output/sub%2fdata.csv")]
    [InlineData("orkeon:///output/sub%2Fdata.csv")]
    [InlineData("orkeon:///output/sub\\data.csv")]
    [InlineData("orkeon:///output/sub%5cdata.csv")]
    [InlineData("orkeon:///output/report.md\u0000.png")]
    [InlineData("orkeon:///output/report.md%00")]
    [InlineData("orkeon:///output//report.md")]
    [InlineData("orkeon:///output/report.md?x=1")]
    [InlineData("orkeon:///output/report.md#frag")]
    public async Task ShouldRefuseTheRead_WhenASegmentCarriesASeparatorOrANul(string uri)
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });

        var response = await SendAsync(server, "resources/read", JsonSerializer.Serialize(new { uri }));

        Assert.NotNull(response.Error);
        Assert.Null(response.Result);
    }

    [Fact]
    public async Task ShouldAnswerAnError_WhenTheUriIsVeryLong()
    {
        var server = Server(Files(), new McpServerOptions { ExposeOutput = true });
        var uri = "orkeon:///output/" + new string('a', 200_000);

        var response = await SendAsync(server, "resources/read", JsonSerializer.Serialize(new { uri }));

        Assert.NotNull(response.Error);
        Assert.Null(response.Result);
    }

    [Fact]
    public async Task ShouldNotFollowASymlinkOutOfOutput_WhenAFileOrADirectoryLinksOutside()
    {
        var root = Directory.CreateTempSubdirectory("orkeon-fwk18-link-");
        try
        {
            var output = Directory.CreateDirectory(Path.Combine(root.FullName, "output"));
            var outside = Directory.CreateDirectory(Path.Combine(root.FullName, "outside"));
            await File.WriteAllTextAsync(Path.Combine(outside.FullName, "secret.txt"), "top secret", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(output.FullName, "ok.txt"), "fine", TestContext.Current.CancellationToken);
            try
            {
                File.CreateSymbolicLink(Path.Combine(output.FullName, "linkfile.txt"), Path.Combine(outside.FullName, "secret.txt"));
                Directory.CreateSymbolicLink(Path.Combine(output.FullName, "linkdir"), outside.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Skip("The platform or the account cannot create symbolic links.");
            }

            var server = Server(new DiskBackedFileSystemService(output.FullName, "/output"), new McpServerOptions { ExposeOutput = true });

            var list = await SendAsync(server, "resources/list");
            var file = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/linkfile.txt"}""");
            var viaDirectory = await SendAsync(server, "resources/read", """{"uri":"orkeon:///output/linkdir/secret.txt"}""");

            Assert.Equal(["orkeon:///output/ok.txt"], Uris(ResultOf(list)));
            foreach (var response in new[] { file, viaDirectory })
            {
                Assert.NotNull(response.Error);
                Assert.DoesNotContain("top secret", JsonSerializer.Serialize(response), StringComparison.Ordinal);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
