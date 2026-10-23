using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Editing.Intent;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing.Intent;

public sealed class EnsureMemberOperationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ensuremember").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;
    private readonly FakeCountingFileSystemService _counting;

    public EnsureMemberOperationTests()
    {
        _disk = new DiskBackedFileSystemService(_dir, "/workspace");
        _counting = new FakeCountingFileSystemService(_disk);
    }

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private async Task SeedAsync(string name, string content)
        => await _disk.WriteAllBytesAsync("/workspace/" + name, Encoding.UTF8.GetBytes(content), CancellationToken.None);

    private async Task<string> ReadAsync(string name)
        => Encoding.UTF8.GetString((await _disk.TryReadAllBytesAsync("/workspace/" + name, CancellationToken.None))!);

    private async Task<EditOutcome> EnsureAsync(string file, string type, string text, EnsureMemberKind kind)
        => await new StructuralEditEngine(_counting, _pool)
            .ApplyAsync("/workspace/" + file, [new EnsureMemberOperation(NodeSelector.Parse(type), text, kind)]);

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldPlaceFieldAfterLastField_WhenEnsureField()
    {
        await SeedAsync("c.cs", "public class C\n{\n    private int _a;\n    private int _b;\n\n    public C() { }\n\n    public int M() { return 1; }\n}\n");
        await SeedAsync("d.cs", "public class D\n{\n    public int M() { return 1; }\n}\n");

        var first = await EnsureAsync("c.cs", "C", "private readonly int _c;", EnsureMemberKind.Field);
        var afterFirst = await ReadAsync("c.cs");
        var writes = _counting.Writes.Count;
        var second = await EnsureAsync("c.cs", "C", "private readonly int _c;", EnsureMemberKind.Field);
        await EnsureAsync("d.cs", "D", "private readonly int _c;", EnsureMemberKind.Field);

        Assert.True(first.Applied);
        Assert.Equal("public class C\n{\n    private int _a;\n    private int _b;\n    private readonly int _c;\n\n    public C() { }\n\n    public int M() { return 1; }\n}\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(afterFirst, await ReadAsync("c.cs"));
        Assert.Equal(writes + 1, _counting.Writes.Count);
        Assert.Equal("public class D\n{\n    private readonly int _c;\n\n    public int M() { return 1; }\n}\n", await ReadAsync("d.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldPlaceFieldAfterLastField_WhenTypeScriptClass()
    {
        await SeedAsync("c.ts", "class C {\n  private name = '';\n  private id = 1;\n\n  constructor() {}\n\n  run(): void {}\n}\n");

        var outcome = await EnsureAsync("c.ts", "C", "private count = 0;", EnsureMemberKind.Field);

        Assert.True(outcome.Applied);
        Assert.Equal("class C {\n  private name = '';\n  private id = 1;\n  private count = 0;\n\n  constructor() {}\n\n  run(): void {}\n}\n", await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfEverywhere_WhenFieldInsertedInCrlfFileWithoutFinalNewline()
    {
        await SeedAsync("c.cs", "public class C\r\n{\r\n    private int _a;\r\n\r\n    public int M() { return 1; }\r\n}");

        await EnsureAsync("c.cs", "C", "private int _b;", EnsureMemberKind.Field);
        await EnsureAsync("c.cs", "C", "public int N()\n{\n    return 2;\n}", EnsureMemberKind.Member);

        Assert.Equal(
            "public class C\r\n{\r\n    private int _a;\r\n    private int _b;\r\n\r\n    public int M() { return 1; }\r\n\r\n    public int N()\r\n    {\r\n        return 2;\r\n    }\r\n}",
            await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfEverywhere_WhenTypeScriptMemberInsertedInCrlfFileWithoutFinalNewline()
    {
        await SeedAsync("c.ts", "class C {\r\n  private a = 1;\r\n\r\n  run(): void {}\r\n}");

        await EnsureAsync("c.ts", "C", "private b = 2;", EnsureMemberKind.Field);
        await EnsureAsync("c.ts", "C", "stop(): void {\n  return;\n}", EnsureMemberKind.Member);

        Assert.Equal(
            "class C {\r\n  private a = 1;\r\n  private b = 2;\r\n\r\n  run(): void {}\r\n\r\n  stop(): void {\r\n    return;\r\n  }\r\n}",
            await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepLfAndNoFinalNewline_WhenMemberInsertedInLfFileWithoutFinalNewline()
    {
        await SeedAsync("c.cs", "public class C\n{\n    private int _a;\n}");

        await EnsureAsync("c.cs", "C", "private int _b;", EnsureMemberKind.Field);

        Assert.Equal("public class C\n{\n    private int _a;\n    private int _b;\n}", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldBeNoOp_WhenFieldAlreadyPresent()
    {
        const string source = "public class C\n{\n    private int _a = 1;\n\n    public int M() { return 1; }\n}\n";
        await SeedAsync("c.cs", source);
        var writes = _counting.Writes.Count;

        var outcome = await EnsureAsync("c.cs", "C", "private long _a;", EnsureMemberKind.Field);

        Assert.False(outcome.Applied);
        Assert.Equal(source, await ReadAsync("c.cs"));
        Assert.Equal(writes, _counting.Writes.Count);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldPlaceMemberWithItsKind_WhenEnsureMemberIsProperty()
    {
        await SeedAsync("p.cs", "public class C\n{\n    private int _a;\n    public int Id { get; set; }\n\n    public int M() { return 1; }\n\n    public int N() { return 2; }\n}\n");
        await SeedAsync("f.cs", "public class C\n{\n    private int _a;\n\n    public int M() { return 1; }\n}\n");
        await SeedAsync("m.cs", "public class C\n{\n    private int _a;\n\n    public int M() { return 1; }\n\n    public int Z { get; set; }\n}\n");
        await SeedAsync("l.cs", "public class C\n{\n    private int _a;\n}\n");

        await EnsureAsync("p.cs", "C", "public int Count { get; set; }", EnsureMemberKind.Member);
        await EnsureAsync("f.cs", "C", "public int Count { get; set; }", EnsureMemberKind.Member);
        await EnsureAsync("m.cs", "C", "public int K() { return 3; }", EnsureMemberKind.Member);
        await EnsureAsync("l.cs", "C", "public int K() { return 3; }", EnsureMemberKind.Member);

        Assert.Equal("public class C\n{\n    private int _a;\n    public int Id { get; set; }\n    public int Count { get; set; }\n\n    public int M() { return 1; }\n\n    public int N() { return 2; }\n}\n", await ReadAsync("p.cs"));
        Assert.Equal("public class C\n{\n    private int _a;\n    public int Count { get; set; }\n\n    public int M() { return 1; }\n}\n", await ReadAsync("f.cs"));
        Assert.Equal("public class C\n{\n    private int _a;\n\n    public int M() { return 1; }\n\n    public int K() { return 3; }\n\n    public int Z { get; set; }\n}\n", await ReadAsync("m.cs"));
        Assert.Equal("public class C\n{\n    private int _a;\n\n    public int K() { return 3; }\n}\n", await ReadAsync("l.cs"));
    }
}
