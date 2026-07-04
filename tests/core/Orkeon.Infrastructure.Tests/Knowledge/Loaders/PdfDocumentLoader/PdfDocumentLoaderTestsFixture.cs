using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Tests.Doubles;
using UglyToad.PdfPig.Writer;

namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class PdfDocumentLoaderTestsFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly MockFileSystemService _fs;
    private readonly PdfDocumentLoader _loader;

    public PdfDocumentLoaderTestsFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdf-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = new MockFileSystemService(_tempDir);
        _loader = new PdfDocumentLoader(_fs);
    }

    private string SavePdf(byte[] bytes)
    {
        var name = $"test_{Guid.NewGuid()}.pdf";
        File.WriteAllBytes(Path.Combine(_tempDir, name), bytes);
        return $"/{name}"; // virtual path
    }

    /// <summary>Creates a temporary PDF file with the given text content on a single page.</summary>
    public string CreateTempPdf(string textContent)
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);

        if (!string.IsNullOrEmpty(textContent))
            page.AddText(textContent, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);

        return SavePdf(builder.Build());
    }

    /// <summary>Creates a temporary PDF file with multiple pages.</summary>
    public string CreateTempMultiPagePdf(params string[] pageTexts)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            if (!string.IsNullOrEmpty(text))
                page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        }

        return SavePdf(builder.Build());
    }

    /// <summary>Creates a temporary PDF with metadata (title, author, creator).</summary>
    public string CreateTempPdfWithMetadata(string textContent, string title, string author, string creator)
    {
        using var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = title;
        builder.DocumentInformation.Author = author;
        builder.DocumentInformation.Creator = creator;

        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);

        if (!string.IsNullOrEmpty(textContent))
            page.AddText(textContent, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);

        return SavePdf(builder.Build());
    }

    /// <summary>Creates an empty PDF (one page, no text).</summary>
    public string CreateEmptyPdf()
    {
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        return SavePdf(builder.Build());
    }

    public Task<LoadedDocument> LoadAsync(string vPath)
        => _loader.LoadAsync(vPath);

    public bool CanLoad(string source)
        => _loader.CanLoad(source);

    public PdfDocumentLoader GetLoader() => _loader;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
