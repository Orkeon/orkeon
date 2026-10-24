using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Fixtures.DevRepo;

namespace Orkeon.Analysis.Tests;

public sealed class AccessCostServiceTests : IDisposable
{
    private const string DefaultPattern =
        @"repositor|service|wrapper|client|provider|unitofwork|dbcontext|dbset|gateway|store|" +
        @"publisher|bus|mediator|cache|queue|adapter|channel|hub|sandbox|filesystem|" +
        @"^(this\.)?_?(db|context|fs)$";

    private const string OrkeonReceivers =
        "toolInvocation|checkpoint|coordinator|orchestrator|shortTerm|longTerm|llm|embedding|a2a|eventHub";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-access-cost-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static IReadOnlyList<RaggableNode> Extract(ILanguageAdapter adapter, string path, string source)
    {
        using var pool = new TreeSitterParserPool();
        return new UniversalSemanticMapper(adapter, pool).ExtractNodes(path, source);
    }

    private static IReadOnlyList<RaggableNode> Members(IReadOnlyList<RaggableNode> nodes)
        => nodes.Where(n => n.Kind is UniversalNodeKind.Method or UniversalNodeKind.Function).ToList();

    private static Regex WithExtra(string extra)
        => new($"{AccessCostService.DefaultReceivers}|{extra}", RegexOptions.IgnoreCase);

    [Fact]
    [Trait("BR", "RT-35")]
    public void ShouldListAwaitedPortCalls_WhenMemberAwaitsRepository()
    {
        const string source =
            "public sealed class Handler\n{\n    public async Task Handle(Command command, CancellationToken ct)\n    {\n" +
            "        var configuration = await _taskRepository.GetByIdAsync(command.Id, ct);\n" +
            "        await _taskRepository.AddAsync(configuration, ct);\n    }\n}\n";
        var members = Members(Extract(new CSharpAdapter(), "/r/Handler.cs", source));

        var report = new AccessCostService().Compute(members, AccessCostService.DefaultReceivers);

        Assert.Equal(DefaultPattern, AccessCostService.DefaultReceivers.ToString());
        Assert.Equal(RegexOptions.IgnoreCase, AccessCostService.DefaultReceivers.Options & RegexOptions.IgnoreCase);
        var member = Assert.Single(report.Members);
        Assert.Collection(member.PortCalls,
            c =>
            {
                Assert.Equal("_taskRepository", c.Receiver);
                Assert.Equal("GetByIdAsync", c.Method);
                Assert.Equal(5, c.Line);
                Assert.False(c.InLoop);
                Assert.False(c.InLambda);
            },
            c =>
            {
                Assert.Equal("_taskRepository", c.Receiver);
                Assert.Equal("AddAsync", c.Method);
                Assert.Equal(6, c.Line);
                Assert.False(c.InLoop);
                Assert.False(c.InLambda);
            });
        Assert.Equal(1, member.Cost.Reads);
        Assert.Equal(1, member.Cost.Writes);
        Assert.Equal(
            "Cost: 1 read + 1 write through ports outside loops; in a loop, a lambda or a memory filter: none",
            report.Cost.Render());
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public void ShouldFlagInLoop_WhenAwaitInsideForeach()
    {
        const string source =
            "public sealed class LoopHandler\n{\n    public async Task Handle(Command command, CancellationToken ct)\n    {\n" +
            "        var signatures = new List<Signature>();\n        foreach (var id in command.SignatureIds)\n        {\n" +
            "            signatures.Add(await signatureRepository.GetSignature(id, ct));\n        }\n" +
            "        await signatureRepository.Save(signatures, ct);\n    }\n}\n";
        var members = Members(Extract(new CSharpAdapter(), "/r/LoopHandler.cs", source));

        var report = new AccessCostService().Compute(members, AccessCostService.DefaultReceivers);

        var member = Assert.Single(report.Members);
        var call = Assert.Single(member.PortCalls, c => c.Method == "GetSignature");
        Assert.Equal("signatureRepository", call.Receiver);
        Assert.Equal(8, call.Line);
        Assert.True(call.InLoop);
        Assert.False(call.InLambda);
        Assert.Equal(1, report.Cost.Loops);
        Assert.Equal(
            "Cost: 0 reads + 1 write through ports outside loops; in a loop, a lambda or a memory filter: 1 - back to design",
            report.Cost.Render());
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public void ShouldFlagInLambda_WhenAwaitInsideLinqLambda()
    {
        const string source =
            "public sealed class LambdaHandler\n{\n    public async Task Handle(Command command, CancellationToken ct)\n    {\n" +
            "        var tasks = command.Ids.Select(async id => await _store.LoadAsync(id, ct));\n" +
            "        await Task.WhenAll(tasks);\n    }\n}\n";
        var members = Members(Extract(new CSharpAdapter(), "/r/LambdaHandler.cs", source));

        var report = new AccessCostService().Compute(members, AccessCostService.DefaultReceivers);

        var call = Assert.Single(Assert.Single(report.Members).PortCalls);
        Assert.Equal("_store", call.Receiver);
        Assert.Equal("LoadAsync", call.Method);
        Assert.Equal(5, call.Line);
        Assert.True(call.InLambda);
        Assert.False(call.InLoop);
        Assert.Equal(1, report.Cost.Lambdas);
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public void ShouldListPortCallsForTypeScript_WhenMemberAwaitsClient()
    {
        const string source =
            "export class Fetcher {\n  async run(id: string) {\n" +
            "    const thing = await this.client.fetchThing(id);\n    return thing;\n  }\n}\n";
        var members = Members(Extract(new TypeScriptAdapter(), "/r/fetcher.ts", source))
            .Where(n => n.Name == "run").ToList();

        var report = new AccessCostService().Compute(members, AccessCostService.DefaultReceivers);

        var call = Assert.Single(Assert.Single(report.Members).PortCalls);
        Assert.Equal("this.client", call.Receiver);
        Assert.Equal("fetchThing", call.Method);
        Assert.Equal(3, call.Line);
        Assert.False(call.InLoop);
        Assert.False(call.InLambda);
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public void ShouldReportInMemoryFilter_WhenWhereAppliedToAwaitedList()
    {
        const string source =
            "public sealed class FilterHandler\n{\n    public async Task<int> Handle(Query query, CancellationToken ct)\n    {\n" +
            "        var open = (await configurationRepository.GetAll(ct)).Where(c => c.IsOpen).ToList();\n" +
            "        return open.Count;\n    }\n}\n";
        var members = Members(Extract(new CSharpAdapter(), "/r/FilterHandler.cs", source));

        var report = new AccessCostService().Compute(members, AccessCostService.DefaultReceivers);

        var filter = Assert.Single(Assert.Single(report.Members).InMemoryFilters);
        Assert.Equal(5, filter.Line);
        Assert.Equal("Where", filter.Method);
        Assert.Equal(1, report.Cost.MemoryFilters);
        Assert.EndsWith("1 - back to design", report.Cost.Render());
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public void ShouldMatchAccessCostGolden_WhenRunOnScriptFixture()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DevRepo", "Goldens", "access-cost.json");
        using var golden = JsonDocument.Parse(File.ReadAllText(goldenPath));
        var root = golden.RootElement;

        var builder = new DevRepoFixtureBuilder();
        foreach (var fixture in root.GetProperty("fixtures").EnumerateArray())
            builder.WithCSharpFile(fixture.GetProperty("path").GetString()!, fixture.GetProperty("content").GetString()!);
        var fs = new DiskBackedFileSystemService(_tempDir, "/repo");
        builder.Build(fs, "/repo");

        List<RaggableNode> Load(params string[] files) => files
            .SelectMany(f => Members(Extract(
                new CSharpAdapter(), "/repo/" + f,
                fs.TryReadAllTextAsync("/repo/" + f, CancellationToken.None).GetAwaiter().GetResult()!)))
            .ToList();

        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        string Expected(int index)
        {
            var cmd = cases[index].GetProperty("cmd").GetString()!;
            var start = cmd.IndexOf("'Cost: ", StringComparison.Ordinal) + 1;
            return cmd[start..cmd.IndexOf('\'', start)];
        }

        void Check(int index, Regex receivers, params string[] files)
        {
            var expected = Expected(index);
            var actual = new AccessCostService().Compute(Load(files), receivers).Cost.Render();
            if (expected.EndsWith("none", StringComparison.Ordinal) || expected.EndsWith("design", StringComparison.Ordinal))
                Assert.Equal(expected, actual);
            else
                Assert.StartsWith(expected, actual);
        }

        var defaults = AccessCostService.DefaultReceivers;
        Check(0, defaults, "cost/FlatHandler.cs");
        Check(2, WithExtra("quotaChecker"), "cost/FlatHandler.cs");
        Check(3, defaults, "cost/LoopHandler.cs");
        Check(6, defaults, "cost/FlatHandler.cs", "cost/LoopHandler.cs");
        Check(7, WithExtra(OrkeonReceivers), "cost/FsHandler.cs");
    }
}
