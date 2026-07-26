using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// Tests for <see cref="InlineTextLoader"/>: claims only explicitly hinted
/// inline text sources and promotes <c>meta.*</c> options to document metadata.
/// </summary>
public class InlineTextLoaderTests
{
    private static SourceDescriptor Source(
        string location = "/docs/a.txt",
        string? kind = InlineTextLoader.TextKind,
        params (string Key, string Value)[] options)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (key, value) in options)
            builder[key] = value;

        return new SourceDescriptor
        {
            Location = location,
            Kind = kind,
            Options = builder.ToImmutable(),
        };
    }

    [Fact]
    public void CanLoad_TextKindWithContent_IsClaimed()
    {
        var loader = new InlineTextLoader();
        Assert.True(loader.CanLoad(Source(options: (InlineTextLoader.ContentOptionKey, "hello"))));
    }

    [Fact]
    public void CanLoad_MissingContentOrOtherKind_IsNotClaimed()
    {
        var loader = new InlineTextLoader();

        // "text" kind but no content payload.
        Assert.False(loader.CanLoad(Source()));
        // Unhinted or file-kind sources belong to the file loaders.
        Assert.False(loader.CanLoad(Source(kind: null, options: (InlineTextLoader.ContentOptionKey, "x"))));
        Assert.False(loader.CanLoad(Source(kind: "file", options: (InlineTextLoader.ContentOptionKey, "x"))));
    }

    [Fact]
    public async Task LoadAsync_YieldsDocumentWithContentAndPromotedMetadata()
    {
        var loader = new InlineTextLoader();
        var source = Source(
            location: "/data/report.pdf#page=3",
            options:
            [
                (InlineTextLoader.ContentOptionKey, "Page three text."),
                (InlineTextLoader.MetadataOptionPrefix + "page_number", "3"),
                (InlineTextLoader.MetadataOptionPrefix + "source_file", "/data/report.pdf"),
                ("unrelated", "dropped"),
            ]);

        var documents = new List<RagDocument>();
        await foreach (var document in loader.LoadAsync(source, TestContext.Current.CancellationToken))
            documents.Add(document);

        var doc = Assert.Single(documents);
        Assert.Equal("/data/report.pdf#page=3", doc.SourceId);
        Assert.Equal("Page three text.", doc.Content);
        Assert.Equal("3", doc.Metadata["page_number"]);
        Assert.Equal("/data/report.pdf", doc.Metadata["source_file"]);
        Assert.False(doc.Metadata.ContainsKey("unrelated"));
        Assert.False(doc.Metadata.ContainsKey(InlineTextLoader.ContentOptionKey));
    }

    [Fact]
    public async Task LoadAsync_ExplicitSourceId_WinsOverLocation()
    {
        var loader = new InlineTextLoader();
        var source = Source(options: (InlineTextLoader.ContentOptionKey, "x"))
            with
        { SourceId = "stable-id" };

        var documents = new List<RagDocument>();
        await foreach (var document in loader.LoadAsync(source, TestContext.Current.CancellationToken))
            documents.Add(document);

        Assert.Equal("stable-id", Assert.Single(documents).SourceId);
    }
}
