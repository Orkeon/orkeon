using ClosedXML.Excel;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Data.Search;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Orkeon.Tools.Data.Tests;

/// <summary>
/// Verifies that the Tools.Data file tools honour the Virtual File System contract:
/// a virtual path (e.g. <c>/work/x.xlsx</c>) that has NO physical counterpart on disk
/// must be resolved through <see cref="IFileSystemService"/>, never opened as a literal
/// OS path. This reproduces the runner scenario where <c>/data</c> and <c>/output</c> are
/// Docker-style VFS mounts that do not exist at the host filesystem root.
///
/// Before the fix, the writers passed the virtual path straight to ClosedXML/OpenXml
/// (physical write → "Could not find a part of the path"), and the readers/pdf_reader
/// validated against the physical workspace root (→ "outside workspace directory").
/// </summary>
public sealed class VfsComplianceTests
{
    private static FakeFileSystemService NewVfs() =>
        new FakeFileSystemService().AddMount("/work", FileAccessRights.ReadWrite);

    // Readers require an IPathValidator alongside the VFS. The VFS branch short-circuits
    // before the validator is consulted, so an allow-all stub is sufficient here.
    private static readonly IPathValidator Validator = new AllowAllPathValidator();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Writers ──────────────────────────────────────────────────────────

    [Fact]
    public async Task XlsxWriter_WritesThroughVfs_ToVirtualPath()
    {
        var fs = NewVfs();
        using var tool = new XlsxWriteTool(fs, Validator);

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = "/work/factures.xlsx",
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Factures",
                        ["headers"] = new List<object> { "fichier", "total_ttc" },
                        ["rows"] = new List<object> { new List<object> { "facture_01.pdf", "1500,00" } }
                    }
                }
            });

        var result = await tool.CallAsync(request, Ct);

        Assert.True(result.Success, result.Error);

        // The bytes must live in the VFS at the virtual path — not on physical disk.
        var bytes = await fs.TryReadAllBytesAsync("/work/factures.xlsx", Ct);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0);
        Assert.False(File.Exists("/work/factures.xlsx"));

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Factures", wb.Worksheet(1).Name);
        Assert.Equal("facture_01.pdf", wb.Worksheet(1).Cell(2, 1).GetString());
    }

    [Fact]
    public async Task DocxWriter_WritesThroughVfs_ToVirtualPath()
    {
        var fs = NewVfs();
        using var tool = new DocxWriteTool(fs, Validator);

        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = "/work/synthese.docx",
                ["title"] = "Synthèse",
                ["paragraphs"] = new List<object> { "Ligne 1", "Ligne 2" }
            });

        var result = await tool.CallAsync(request, Ct);

        Assert.True(result.Success, result.Error);
        var bytes = await fs.TryReadAllBytesAsync("/work/synthese.docx", Ct);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0);
        Assert.False(File.Exists("/work/synthese.docx"));
    }

    // ── Readers (full VFS round-trip with the matching writer) ────────────

    [Fact]
    public async Task XlsxReader_ReadsThroughVfs_FromVirtualPath()
    {
        var fs = NewVfs();
        using (var writer = new XlsxWriteTool(fs, Validator))
        {
            await writer.CallAsync(new ToolCallRequest("xlsx_writer", new Dictionary<string, object?>
            {
                ["file_path"] = "/work/r.xlsx",
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "People",
                        ["headers"] = new List<object> { "Name", "Age" },
                        ["rows"] = new List<object> { new List<object> { "Alice", "30" } }
                    }
                }
            }), Ct);
        }

        using var reader = new XlsxReadTool(fs, Validator);
        var result = await reader.CallAsync(new ToolCallRequest("xlsx_reader",
            new Dictionary<string, object?> { ["file_path"] = "/work/r.xlsx" }), Ct);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists("/work/r.xlsx"));
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, Convert.ToInt32(dict!["sheet_count"]!));
    }

    [Fact]
    public async Task DocxReader_ReadsThroughVfs_FromVirtualPath()
    {
        var fs = NewVfs();
        using (var writer = new DocxWriteTool(fs, Validator))
        {
            await writer.CallAsync(new ToolCallRequest("docx_writer", new Dictionary<string, object?>
            {
                ["file_path"] = "/work/r.docx",
                ["paragraphs"] = new List<object> { "Hello VFS" }
            }), Ct);
        }

        using var reader = new DocxReadTool(fs, Validator);
        var result = await reader.CallAsync(new ToolCallRequest("docx_reader",
            new Dictionary<string, object?> { ["file_path"] = "/work/r.docx" }), Ct);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists("/work/r.docx"));
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("Hello VFS", dict!["content"]?.ToString());
    }

    [Fact]
    public async Task PdfReader_ReadsThroughVfs_FromVirtualPath()
    {
        var fs = NewVfs();
        fs.AddFile("/work/doc.pdf", BuildPdf("Bonjour VFS PDF"));

        using var tool = new PdfReaderTool(fs, Validator);
        var result = await tool.CallAsync(new ToolCallRequest("pdf_reader",
            new Dictionary<string, object?> { ["path"] = "/work/doc.pdf" }), Ct);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists("/work/doc.pdf"));
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("Bonjour", dict!["content"]?.ToString());
    }

    // ── Parsers / search ─────────────────────────────────────────────────

    [Fact]
    public async Task XmlParser_ReadsFileThroughVfs_FromVirtualPath()
    {
        var fs = NewVfs();
        fs.AddFile("/work/config.xml", "<catalog><book>One</book></catalog>");

        using var tool = new XmlParserTool(fs, Validator);
        var result = await tool.CallAsync(new ToolCallRequest("xml_parser",
            new Dictionary<string, object?> { ["input"] = "/work/config.xml" }), Ct);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("catalog", dict!["root_element"]?.ToString());
        Assert.Equal("file", dict["source"]?.ToString());
    }

    [Fact]
    public async Task JsonTool_ReadsFileThroughVfs_WhenInputIsVirtualPath()
    {
        var fs = NewVfs();
        fs.AddFile("/work/data.json", "[{\"fichier\":\"facture_01.pdf\"}]");

        using var tool = new JsonTool(fs);
        var result = await tool.CallAsync(new ToolCallRequest("json_tool",
            new Dictionary<string, object?> { ["input"] = "/work/data.json", ["operation"] = "parse" }), Ct);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True((bool)dict!["parsed"]!);
    }

    [Fact]
    public async Task JsonTool_StillParsesInlineJson_WhenInputIsNotAPath()
    {
        var fs = NewVfs();
        using var tool = new JsonTool(fs);
        var result = await tool.CallAsync(new ToolCallRequest("json_tool",
            new Dictionary<string, object?> { ["input"] = "{\"a\":1}", ["operation"] = "parse" }), Ct);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task PdfSearch_ReadsPdfBytesThroughVfs_FromVirtualPath()
    {
        var fs = NewVfs();
        fs.AddFile("/work/doc.pdf", BuildPdf("Le chat dort sur le tapis. Orkeon teste la recherche."));

        using var tool = new PdfSearchTool(new ConstantEmbeddingService(), fs);
        var result = await tool.CallAsync(new ToolCallRequest("pdf_search",
            new Dictionary<string, object?> { ["path"] = "/work/doc.pdf", ["query"] = "chat" }), Ct);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, Convert.ToInt32(dict!["files_processed"]!));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static byte[] BuildPdf(string text)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }

    private sealed class ConstantEmbeddingService : IEmbeddingService
    {
        private static readonly float[] Vector = [1f, 0f, 0f];

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Vector);
    }

    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
            => PathValidationResult.Allowed(requestedPath);
    }
}
