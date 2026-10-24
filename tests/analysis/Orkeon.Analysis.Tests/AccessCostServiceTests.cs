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

    private static RaggableNode MemberWith(params StatementNode[] statements)
    {
        var node = new RaggableNode
        {
            Id = "m1",
            Kind = UniversalNodeKind.Method,
            Name = "Handle",
            VirtualFilePath = "/r/Handler.cs",
            Range = new NodeRange(0, 10, 1, 10, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "csharp",
            SourceSnippet = string.Empty,
            Sha256 = string.Empty,
        };
        node.StatementsMutable.AddRange(statements);
        return node;
    }

    private static StatementNode Statement(
        StatementKind kind, string expression, string? receiver = null, int line = 3,
        bool inLoop = false, bool inLambda = false, params StatementNode[] children)
    {
        var statement = new StatementNode
        {
            Id = "s" + line,
            ParentSymbolId = "m1",
            Kind = kind,
            StartLine = line,
            Expression = expression,
            Receiver = receiver,
            InLoop = inLoop,
            InLambda = inLambda,
        };
        foreach (var child in children) statement.AddChild(child);
        return statement;
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Add")]
    [InlineData("Insert")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Remove")]
    [InlineData("Persist")]
    [InlineData("Publish")]
    [InlineData("Send")]
    [InlineData("Commit")]
    [InlineData("Create")]
    [InlineData("Put")]
    [InlineData("Post")]
    [InlineData("Write")]
    [InlineData("Upsert")]
    [InlineData("Store")]
    [InlineData("Execute")]
    [InlineData("Transfer")]
    [InlineData("Append")]
    [InlineData("Set")]
    [InlineData("Enqueue")]
    [InlineData("Dispatch")]
    [InlineData("Ingest")]
    public void ShouldCountWrite_WhenAwaitedMethodStartsWithWriteVerb(string verb)
    {
        var member = MemberWith(Statement(StatementKind.Await, $"await _orderRepository.{verb}Async(order, ct)", "_orderRepository"));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        var call = Assert.Single(Assert.Single(report.Members).PortCalls);
        Assert.Equal(verb + "Async", call.Method);
        Assert.Equal(1, report.Cost.Writes);
        Assert.Equal(0, report.Cost.Reads);
    }

    [Theory]
    [InlineData("GetByIdAsync")]
    [InlineData("FindAsync")]
    [InlineData("ListAsync")]
    [InlineData("savedAsync")]
    [InlineData("Sav")]
    [InlineData("QueryAsync")]
    [InlineData("CountAsync")]
    [InlineData("LoadAsync")]
    [InlineData("ReadAsync")]
    [InlineData("TryGetAsync")]
    public void ShouldCountRead_WhenAwaitedMethodDoesNotStartWithWriteVerb(string method)
    {
        var member = MemberWith(Statement(StatementKind.Await, $"await _store.{method}(id)", "_store"));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        Assert.Equal(1, report.Cost.Reads);
        Assert.Equal(0, report.Cost.Writes);
    }

    [Theory]
    [InlineData("_orderRepository", true)]
    [InlineData("userService", true)]
    [InlineData("_httpWrapper", true)]
    [InlineData("HttpClient", true)]
    [InlineData("_tokenProvider", true)]
    [InlineData("_unitOfWork", true)]
    [InlineData("_appDbContext", true)]
    [InlineData("users.DbSet", true)]
    [InlineData("_paymentGateway", true)]
    [InlineData("_blobStore", true)]
    [InlineData("_eventPublisher", true)]
    [InlineData("_messageBus", true)]
    [InlineData("_mediator", true)]
    [InlineData("_memoryCache", true)]
    [InlineData("_workQueue", true)]
    [InlineData("_mailAdapter", true)]
    [InlineData("_channel", true)]
    [InlineData("_notificationHub", true)]
    [InlineData("_sandbox", true)]
    [InlineData("_fileSystem", true)]
    [InlineData("_db", true)]
    [InlineData("db", true)]
    [InlineData("this._context", true)]
    [InlineData("this.fs", true)]
    [InlineData("_fs", true)]
    [InlineData("_logger", false)]
    [InlineData("_dbx", false)]
    [InlineData("that._context", false)]
    [InlineData("_clock", false)]
    public void ShouldRecognisePortReceiver_WhenDefaultPatternApplies(string receiver, bool expected)
    {
        var member = MemberWith(Statement(StatementKind.Await, $"await {receiver}.GetAsync()", receiver));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        Assert.Equal(expected ? 1 : 0, Assert.Single(report.Members).PortCalls.Count);
    }

    [Theory]
    [InlineData("Where")]
    [InlineData("Any")]
    [InlineData("All")]
    [InlineData("First")]
    [InlineData("FirstOrDefault")]
    [InlineData("Single")]
    [InlineData("SingleOrDefault")]
    [InlineData("Count")]
    [InlineData("Last")]
    [InlineData("LastOrDefault")]
    [InlineData("Contains")]
    [InlineData("OrderBy")]
    [InlineData("OrderByDescending")]
    [InlineData("Skip")]
    [InlineData("Take")]
    public void ShouldReportMemoryFilter_WhenLinqOperatorFollowsAwaitedList(string method)
    {
        var member = MemberWith(Statement(StatementKind.FunctionCall, $"(await _repository.ListAsync(ct)).{method}(x => x.IsActive)", line: 7));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        var filter = Assert.Single(Assert.Single(report.Members).InMemoryFilters);
        Assert.Equal(method, filter.Method);
        Assert.Equal(7, filter.Line);
        Assert.Equal(1, report.Cost.MemoryFilters);
    }

    [Theory]
    [InlineData("( await _repository.ListAsync() ) .Where <Order> (x => x.Ok)", 1)]
    [InlineData("(await _repository.ListAsync()).Where(x => x.Ok).ToList()", 0)]
    [InlineData("(await _repository.ListAsync()).Where(x => x.Ok", 0)]
    [InlineData("(await _repository.ListAsync()).Select(x => x.Id)", 0)]
    [InlineData("_repository.List().Where(x => x.Ok)", 0)]
    [InlineData("(awaited.List()).Where(x => x.Ok)", 0)]
    public void ShouldReportMemoryFilter_OnlyWhenFilterIsOutermostCallOnAwaitedValue(string expression, int expected)
    {
        var member = MemberWith(Statement(StatementKind.FunctionCall, expression));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        Assert.Equal(expected, report.Cost.MemoryFilters);
    }

    [Fact]
    public void ShouldIgnoreFilter_WhenStatementIsNotAFunctionCall()
    {
        var member = MemberWith(Statement(StatementKind.Assignment, "(await _repository.ListAsync()).Where(x => x.Ok)"));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        Assert.Equal(0, report.Cost.MemoryFilters);
    }

    [Theory]
    [InlineData("await _repository.Query<Order>(spec).ToListAsync(ct)", "ToListAsync")]
    [InlineData("await _repository.GetAsync(Map(x.Value), ct)", "GetAsync")]
    [InlineData("await _repository.Items.CountAsync()", "CountAsync")]
    [InlineData("await _repository", "?")]
    public void ShouldNameOutermostMethod_WhenAwaitedExpressionChainsCalls(string expression, string expected)
    {
        var member = MemberWith(Statement(StatementKind.Await, expression, "_repository"));

        var report = new AccessCostService().Compute([member], AccessCostService.DefaultReceivers);

        Assert.Equal(expected, Assert.Single(Assert.Single(report.Members).PortCalls).Method);
    }

    [Fact]
    public void ShouldSplitCostsByContext_WhenNestedStatementsAwaitInLoopsAndLambdas()
    {
        var loop = Statement(StatementKind.Await, "await _repository.SaveAsync(x)", "_repository", line: 5, inLoop: true);
        var lambda = Statement(StatementKind.Await, "await _repository.GetAsync(x)", "_repository", line: 6, inLambda: true);
        var parent = Statement(StatementKind.For, "foreach (var x in items)", line: 4, children: [loop, lambda]);
        var member = MemberWith(parent, Statement(StatementKind.Await, "await _repository.GetAsync(1)", "_repository", line: 8));
        var other = MemberWith(Statement(StatementKind.Await, "await _repository.DeleteAsync(1)", "_repository", line: 2));

        var report = new AccessCostService().Compute([member, other], AccessCostService.DefaultReceivers);

        Assert.Equal(2, report.Members.Count);
        Assert.Equal(3, report.Members[0].PortCalls.Count);
        Assert.Equal(1, report.Cost.Reads);
        Assert.Equal(1, report.Cost.Writes);
        Assert.Equal(1, report.Cost.Loops);
        Assert.Equal(1, report.Cost.Lambdas);
    }
}
