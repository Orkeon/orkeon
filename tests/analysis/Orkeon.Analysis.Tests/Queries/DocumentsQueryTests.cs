using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

public sealed class DocumentsQueryTests : IAsyncLifetime
{
    private string _dir = string.Empty;
    private InMemoryRaggableStore _store = null!;
    private string _orderServiceFqn = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _dir = await TestFixtures.WriteDirectoryAsync([
            ("src/Orders.cs", "public class OrderService { }\npublic class Invoice { }\n"),
            ("docs/guide.md", "# Guide\n\nCall `OrderService` first.\n\n## Billing\n\nThen `Invoice` and `OrderService`.\n"),
            ("docs/other.md", "# Other\n\nSee `OrderService`.\n"),
            ("docs/empty.md", "No heading and no code here.\n"),
        ]);
        var fs = TestFixtures.CreateFs(_dir);
        var result = await new RaggableTreeBuilder(new CSharpAdapter(), fs).BuildAsync(
            TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        _store = new InMemoryRaggableStore(result.Tree, fs);
        _orderServiceFqn = result.Tree.Nodes.Single(n => n.Kind == UniversalNodeKind.Class && n.Name == "OrderService").Fqn;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_dir, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ShouldListDocumentingSectionsSortedByPath_WhenSymbolIsDocumented()
    {
        var result = await DocumentsQuery.DocumentedInAsync(_store, _orderServiceFqn, CancellationToken.None);

        Assert.Empty(result.Unresolved);
        Assert.Equal(
            ["/src/docs/guide.md|Guide|1", "/src/docs/guide.md|Guide > Billing|5", "/src/docs/other.md|Other|1"],
            result.Sections.Select(s => $"{s.Path}|{s.HeadingPath}|{s.StartLine}"));
    }

    [Fact]
    public async Task ShouldReportUnresolved_WhenFqnIsUnknown()
    {
        var result = await DocumentsQuery.DocumentedInAsync(_store, "/src/Nope.cs::Nope", CancellationToken.None);

        Assert.Equal(["/src/Nope.cs::Nope"], result.Unresolved);
        Assert.Empty(result.Sections);
    }

    [Fact]
    public async Task ShouldListDocumentedSymbolsSortedByFqn_WhenFileHasSections()
    {
        var result = await DocumentsQuery.DocumentsOfAsync(_store, "docs\\guide.md", TestFixtures.TestVirtualRoot + "/", CancellationToken.None);

        Assert.Empty(result.Unresolved);
        Assert.Equal(
            ["Invoice|Class|Guide > Billing", "OrderService|Class|Guide", "OrderService|Class|Guide > Billing"],
            result.Symbols.Select(s => $"{s.Fqn[(s.Fqn.LastIndexOf(':') + 1)..]}|{s.Kind}|{s.HeadingPath}"));
        Assert.All(result.Symbols, s => Assert.Equal("/src/src/Orders.cs", s.File));
    }

    [Theory]
    [InlineData("docs/empty.md")]
    [InlineData("docs/missing.md")]
    public async Task ShouldReportPathUnresolved_WhenFileHasNoSection(string path)
    {
        var result = await DocumentsQuery.DocumentsOfAsync(_store, path, TestFixtures.TestVirtualRoot, CancellationToken.None);

        Assert.Equal([path], result.Unresolved);
        Assert.Empty(result.Symbols);
    }

    [Fact]
    public async Task ShouldThrow_WhenArgumentsAreNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentsQuery.DocumentedInAsync(null!, "x", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentsQuery.DocumentedInAsync(_store, null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentsQuery.DocumentsOfAsync(null!, "x", "/src", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentsQuery.DocumentsOfAsync(_store, null!, "/src", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentsQuery.DocumentsOfAsync(_store, "x", null!, CancellationToken.None));
    }
}
