using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

[Collection("EditFileToolSharedPaths")]
public sealed class EditFileToolEncodingTests
{
    private const string File = "/workspace/a.txt";
    private const FileAccessRights All = FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create;
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static async Task<ToolCallResponse> EditAsync(IFileSystemService fs, string oldString, string newString, string path = File)
    {
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());
        return await tool.CallAsync(new ToolCallRequest("edit_file", new Dictionary<string, object?>
        {
            [ParamPath] = path, ["old_string"] = oldString, ["new_string"] = newString,
        }));
    }

    private static byte[] Join(byte[] a, byte[] b) => [.. a, .. b];

    private static byte[] Bytes(string text, bool bom = false) =>
        bom ? [.. Bom, .. new UTF8Encoding(false).GetBytes(text)] : new UTF8Encoding(false).GetBytes(text);

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldPreserveBomAndCrlf_WhenEditingFile()
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        fs.AddFile(File, Bytes("a\r\nOLD\r\nb\r\n", bom: true));

        var response = await EditAsync(fs, "OLD", "NEW");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal(Bytes("a\r\nNEW\r\nb\r\n", bom: true), await fs.Inner.TryReadAllBytesAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldMatchOldStringWithCrlfNormalised_WhenFileUsesCrlf()
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        fs.AddFile(File, Bytes("x\r\nfoo\r\nbar\r\ny"));

        var response = await EditAsync(fs, "foo\nbar", "one\ntwo");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal(Bytes("x\r\none\r\ntwo\r\ny"), await fs.Inner.TryReadAllBytesAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRestoreOriginalAndFail_WhenWriteFails()
    {
        var fs = new FakeTruncatingFailingWriteFileSystemService();
        fs.Inner.AddMount("/workspace", All);
        fs.Inner.AddFile(File, "ORIGINAL");

        var response = await EditAsync(fs, "ORIGINAL", "CHANGED");

        Assert.False(response.Success);
        Assert.Contains("simulated disk failure", response.Error);
        Assert.Equal("ORIGINAL", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRestoreOriginal_WhenWriteThrowsNonIoException()
    {
        var fs = new FakeTruncatingFailingWriteFileSystemService
        {
            FirstWriteFailure = () => new InvalidOperationException("simulated invalid state"),
        };
        fs.Inner.AddMount("/workspace", All);
        fs.Inner.AddFile(File, "ORIGINAL");

        var response = await EditAsync(fs, "ORIGINAL", "CHANGED");

        Assert.False(response.Success);
        Assert.Contains("simulated invalid state", response.Error);
        Assert.Equal("ORIGINAL", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReportWriteError_WhenRestoreAlsoFails()
    {
        var fs = new FakeTruncatingFailingWriteFileSystemService
        {
            LaterWriteFailure = () => new IOException("simulated restore failure"),
        };
        fs.Inner.AddMount("/workspace", All);
        fs.Inner.AddFile(File, "ORIGINAL");

        var response = await EditAsync(fs, "ORIGINAL", "CHANGED");

        Assert.False(response.Success);
        Assert.Contains("simulated disk failure", response.Error);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldKeepUntouchedLineEndings_WhenFileHasMixedEndings()
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        fs.AddFile("/workspace/mixed1.txt", Bytes("a\r\nb\nc\nd\r\n"));
        fs.AddFile("/workspace/mixed2.txt", Bytes("a\r\nb\nc\nd\r\n"));
        var ct = TestContext.Current.CancellationToken;

        var edited = await EditAsync(fs, "c", "X", "/workspace/mixed1.txt");
        Assert.True(edited.Success, edited.Error ?? "Expected success");
        Assert.Equal(Bytes("a\r\nb\nX\nd\r\n"), await fs.Inner.TryReadAllBytesAsync("/workspace/mixed1.txt", ct));

        // Mixed endings: old_string is matched literally, no CRLF normalisation.
        var literal = await EditAsync(fs, "b\nc", "b\nY", "/workspace/mixed2.txt");
        Assert.True(literal.Success, literal.Error ?? "Expected success");
        Assert.Equal(Bytes("a\r\nb\nY\nd\r\n"), await fs.Inner.TryReadAllBytesAsync("/workspace/mixed2.txt", ct));

        var crlfMiss = await EditAsync(fs, "a\nb", "Z", "/workspace/mixed2.txt");
        Assert.False(crlfMiss.Success);
        Assert.Contains("not found", crlfMiss.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Bytes("a\r\nb\nY\nd\r\n"), await fs.Inner.TryReadAllBytesAsync("/workspace/mixed2.txt", ct));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuseAndLeaveFileUntouched_WhenFileIsNotValidUtf8AndHasNoBom()
    {
        byte[] latin1 = [0x63, 0xE9, 0x66];
        var fs = new FakeTruncatingFailingWriteFileSystemService { FirstWriteFailure = null };
        fs.Inner.AddMount("/workspace", All);
        fs.Inner.AddFile(File, latin1);

        var response = await EditAsync(fs, "f", "g");

        Assert.False(response.Success);
        Assert.Contains("UTF-8", response.Error);
        Assert.Equal(latin1, await fs.Inner.TryReadAllBytesAsync(File, TestContext.Current.CancellationToken));
        Assert.Equal(0, fs.WriteCalls);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldDecodeUtf32LeBomAsUtf32_WhenEditing()
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        var encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: false);
        byte[] bom = [0xFF, 0xFE, 0x00, 0x00];
        fs.AddFile(File, Join(bom, encoding.GetBytes("abc")));

        var response = await EditAsync(fs, "b", "X");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal(Join(bom, encoding.GetBytes("aXc")), await fs.Inner.TryReadAllBytesAsync(File, TestContext.Current.CancellationToken));
    }

    [Theory]
    [Trait("BR", "RT-09a")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldRoundTripUtf16Bom_WhenEditing(bool bigEndian)
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: false);
        byte[] bom = bigEndian ? [0xFE, 0xFF] : [0xFF, 0xFE];
        fs.AddFile(File, Join(bom, encoding.GetBytes("abc")));

        var response = await EditAsync(fs, "b", "X");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal(Join(bom, encoding.GetBytes("aXc")), await fs.Inner.TryReadAllBytesAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldNameDetectedEncoding_WhenDecodingFailsAfterBom()
    {
        byte[] invalid = [0xFF, 0xFE, 0x61, 0x00, 0x62];
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        fs.AddFile(File, invalid);

        var response = await EditAsync(fs, "a", "b");

        Assert.False(response.Success);
        Assert.Contains("UTF-16", response.Error);
        Assert.DoesNotContain("no byte-order mark", response.Error);
        Assert.Equal(invalid, await fs.Inner.TryReadAllBytesAsync(File, TestContext.Current.CancellationToken));
    }
}
