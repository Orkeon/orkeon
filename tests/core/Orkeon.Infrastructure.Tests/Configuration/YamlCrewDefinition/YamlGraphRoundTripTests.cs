using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>FWK-28 (ADR-018, point 9): import, export and import give the same graph, and the export writes <c>graph:</c>, never <c>graphConfig:</c>.</summary>
public sealed class YamlGraphRoundTripTests
{
    private readonly YamlCrewDefinitionLoader _loader;
    private readonly YamlCrewExporter _exporter;

    public YamlGraphRoundTripTests()
    {
        var serializer = new YamlDotNetSerializer();
        var fs = new FakeFileSystemService();
        _loader = new YamlCrewDefinitionLoader(serializer, fs, NullLogger<YamlCrewDefinitionLoader>.Instance);
        _exporter = new YamlCrewExporter(serializer, fs, NullLogger<YamlCrewExporter>.Instance);
    }

    private async System.Threading.Tasks.Task<(CrewConfiguration First, string Text, CrewConfiguration Second)> TripAsync(string yaml)
    {
        var first = await _loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        var text = _exporter.ExportToString(first);
        var second = await _loader.LoadFromStringAsync(text, TestContext.Current.CancellationToken);
        return (first, text, second);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldGiveTheSameGraph_AfterImportExportImport()
    {
        var (first, _, second) = await TripAsync(YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching));

        CrewConfigurationProjection.AssertEqual(first, second);
        Assert.Equal(first.GraphConfig!.Edges.Count, second.GraphConfig!.Edges.Count);
        Assert.NotNull(second.GraphConfig.Edges[0].When);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldWriteTheGraphBlock_AndNotTheRetiredOne()
    {
        var (_, text, _) = await TripAsync(YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching));

        Assert.Contains("graph:", text, StringComparison.Ordinal);
        Assert.Contains("maxVisits: 5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("graphConfig", text, StringComparison.Ordinal);
        Assert.DoesNotContain("maxStateVisits", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldKeepAnImplicitGraphImplicit()
    {
        var (first, _, second) = await TripAsync(YamlGraphTestCrews.Triage("graph:\n  maxVisits: 4"));

        Assert.Null(second.GraphConfig!.Nodes);
        Assert.Empty(second.GraphConfig.Edges);
        CrewConfigurationProjection.AssertEqual(first, second);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseToExportAnInvalidTopology()
    {
        var config = await _loader.LoadFromStringAsync(
            YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching), TestContext.Current.CancellationToken);
        var broken = config with
        {
            GraphConfig = config.GraphConfig! with { Edges = [.. config.GraphConfig.Edges.Where(e => e.From != "escalate")] },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _exporter.ExportToString(broken));

        Assert.Contains("escalate", ex.Message, StringComparison.Ordinal);
    }
}
