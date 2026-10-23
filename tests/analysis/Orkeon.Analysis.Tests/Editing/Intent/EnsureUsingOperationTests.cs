using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Editing.Intent;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing.Intent;

public sealed class EnsureUsingOperationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ensureusing").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;
    private readonly FakeCountingFileSystemService _counting;

    public EnsureUsingOperationTests()
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

    private async Task<EditOutcome> EnsureAsync(string file, string name, string? module = null)
        => await new StructuralEditEngine(_counting, _pool)
            .ApplyAsync("/workspace/" + file, [new EnsureUsingOperation(name, module)]);

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldAddOneUsingAtSortedPlace_WhenEnsureUsingCalledTwice()
    {
        await SeedAsync("c.cs", "using System;\nusing System.Text;\nusing Zeta;\n\npublic class C\n{\n}\n");

        var first = await EnsureAsync("c.cs", "System.Linq");
        var afterFirst = await ReadAsync("c.cs");
        var writesAfterFirst = _counting.Writes.Count;
        var second = await EnsureAsync("c.cs", "System.Linq");

        Assert.True(first.Applied);
        Assert.Equal("using System;\nusing System.Linq;\nusing System.Text;\nusing Zeta;\n\npublic class C\n{\n}\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(afterFirst, await ReadAsync("c.cs"));
        Assert.Equal(writesAfterFirst, _counting.Writes.Count);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepSystemFirstOrder_WhenFileSortsSystemFirst()
    {
        await SeedAsync("c.cs", "global using Global.One;\n\nusing System;\nusing System.Text;\nusing Acme.Core;\nusing Zeta;\n\npublic class C\n{\n}\n");

        await EnsureAsync("c.cs", "Microsoft.Extensions.Logging");
        await EnsureAsync("c.cs", "System.Collections");

        Assert.Equal(
            "global using Global.One;\n\nusing System;\nusing System.Collections;\nusing System.Text;\nusing Acme.Core;\nusing Microsoft.Extensions.Logging;\nusing Zeta;\n\npublic class C\n{\n}\n",
            await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldCreateUsingBlock_WhenFileHasNone()
    {
        await SeedAsync("h.cs", "// header\npublic class C\n{\n}\n");
        await SeedAsync("n.cs", "public class C\n{\n}\n");

        await EnsureAsync("h.cs", "System.Linq");
        await EnsureAsync("n.cs", "System.Linq");

        Assert.Equal("// header\nusing System.Linq;\n\npublic class C\n{\n}\n", await ReadAsync("h.cs"));
        Assert.Equal("using System.Linq;\n\npublic class C\n{\n}\n", await ReadAsync("n.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldAddOneImport_WhenEnsureImportCalledTwiceOnTypeScript()
    {
        await SeedAsync("a.ts", "import { A } from './a';\nimport { Z } from './z';\n\nexport const x = 1;\n");
        await SeedAsync("m.ts", "import { Bar } from './foo';\n\nexport const x = 1;\n");
        await SeedAsync("h.ts", "// header\nexport const x = 1;\n");

        var first = await EnsureAsync("a.ts", "Foo", "./foo");
        var afterFirst = await ReadAsync("a.ts");
        var writesAfterFirst = _counting.Writes.Count;
        var second = await EnsureAsync("a.ts", "Foo", "./foo");
        await EnsureAsync("m.ts", "Foo", "./foo");
        await EnsureAsync("h.ts", "Foo", "./foo");

        Assert.True(first.Applied);
        Assert.Equal("import { A } from './a';\nimport { Foo } from './foo';\nimport { Z } from './z';\n\nexport const x = 1;\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(afterFirst, await ReadAsync("a.ts"));
        Assert.Equal(writesAfterFirst + 2, _counting.Writes.Count);
        Assert.Equal("import { Bar, Foo } from './foo';\n\nexport const x = 1;\n", await ReadAsync("m.ts"));
        Assert.Equal("// header\nimport { Foo } from './foo';\n\nexport const x = 1;\n", await ReadAsync("h.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldReportRangeAndHashOfExistingUsing_WhenEnsureUsingIsNoOp()
    {
        const string source = "using System;\nclass C {}\n";
        await SeedAsync("c.cs", source);

        var outcome = await EnsureAsync("c.cs", "System");

        var bytes = Encoding.UTF8.GetBytes(source);
        var slice = bytes.AsSpan(outcome.Range.StartByte, outcome.Range.EndByte - outcome.Range.StartByte);
        Assert.False(outcome.Applied);
        Assert.Equal("using System;", Encoding.UTF8.GetString(slice));
        Assert.Equal(1, outcome.Range.StartLine);
        Assert.Equal(1, outcome.Range.EndLine);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(slice)), outcome.NewHash);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldReportOneBasedLines_WhenUsingIsInserted()
    {
        await SeedAsync("c.cs", "using A;\nusing C;\n");

        var outcome = await EnsureAsync("c.cs", "B");

        var bytes = Encoding.UTF8.GetBytes(await ReadAsync("c.cs"));
        var slice = bytes.AsSpan(outcome.Range.StartByte, outcome.Range.EndByte - outcome.Range.StartByte);
        Assert.True(outcome.Applied);
        Assert.Equal("using B;", Encoding.UTF8.GetString(slice));
        Assert.Equal(2, outcome.Range.StartLine);
        Assert.Equal(2, outcome.Range.EndLine);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldRefuse_WhenEnsureImportTargetsMarkdown()
    {
        const string source = "# Doc\n";
        await SeedAsync("doc.md", source);

        await Assert.ThrowsAsync<ArgumentException>(() => EnsureAsync("doc.md", "X", "m"));

        Assert.Equal(source, await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldReportExactRangeAndHash_WhenLastUsingHasCrlfAndNoFinalNewline()
    {
        const string source = "using A;\r\nusing System;";
        await SeedAsync("c.cs", source);

        var outcome = await EnsureAsync("c.cs", "System");

        var slice = Encoding.UTF8.GetBytes(source).AsSpan(outcome.Range.StartByte, outcome.Range.EndByte - outcome.Range.StartByte);
        Assert.False(outcome.Applied);
        Assert.Equal("using System;", Encoding.UTF8.GetString(slice));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(slice)), outcome.NewHash);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfOnEveryLineAndNoFinalNewline_WhenUsingInsertedAfterUnterminatedLastLine()
    {
        await SeedAsync("c.cs", "using A;\r\nusing B;");

        await EnsureAsync("c.cs", "C");

        Assert.Equal("using A;\r\nusing B;\r\nusing C;", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfOnEveryLineAndNoFinalNewline_WhenImportInsertedAfterUnterminatedLastLine()
    {
        await SeedAsync("c.ts", "import {a} from 'a';\r\nimport {b} from 'b';");

        await EnsureAsync("c.ts", "c", "c");

        Assert.Equal("import {a} from 'a';\r\nimport {b} from 'b';\r\nimport { c } from 'c';", await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldLeaveLfFileWithoutFinalNewlineUnchanged_WhenUsingInsertedAfterLastLine()
    {
        await SeedAsync("c.cs", "using A;\nusing B;");

        await EnsureAsync("c.cs", "C");

        Assert.Equal("using A;\nusing B;\nusing C;", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfInTheMiddle_WhenUsingInsertedBetweenUsings()
    {
        await SeedAsync("c.cs", "using A;\r\nusing C;\r\nclass X {}\r\n");

        await EnsureAsync("c.cs", "B");

        Assert.Equal("using A;\r\nusing B;\r\nusing C;\r\nclass X {}\r\n", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfAndNoFinalNewline_WhenBlockCreatedAfterGlobalUsingsOnly()
    {
        await SeedAsync("c.cs", "global using A;\r\nglobal using B;");

        await EnsureAsync("c.cs", "X");

        Assert.Equal("global using A;\r\nglobal using B;\r\n\r\nusing X;", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfAndNoFinalNewline_WhenBlockCreatedAfterHeaderCommentOnly()
    {
        await SeedAsync("c.cs", "// a\r\n// b");

        await EnsureAsync("c.cs", "X");

        Assert.Equal("// a\r\n// b\r\nusing X;", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepLfAndNoFinalNewline_WhenBlockCreatedAfterHeaderCommentOnly()
    {
        await SeedAsync("c.cs", "// a\n// b");

        await EnsureAsync("c.cs", "X");

        Assert.Equal("// a\n// b\nusing X;", await ReadAsync("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfAndNoFinalNewline_WhenImportBlockCreatedAfterHeaderCommentOnlyOnTypeScript()
    {
        await SeedAsync("c.ts", "// a\r\n// b");

        await EnsureAsync("c.ts", "X", "x");

        Assert.Equal("// a\r\n// b\r\nimport { X } from 'x';", await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldReportExactRangeAndHash_WhenFileMixesLineEndings()
    {
        const string source = "using A;\nusing System;\r\nclass C {}\r\n";
        await SeedAsync("c.cs", source);
        var bytes = Encoding.UTF8.GetBytes(source);

        var system = await EnsureAsync("c.cs", "System");
        var a = await EnsureAsync("c.cs", "A");

        var systemSlice = bytes.AsSpan(system.Range.StartByte, system.Range.EndByte - system.Range.StartByte);
        var aSlice = bytes.AsSpan(a.Range.StartByte, a.Range.EndByte - a.Range.StartByte);
        Assert.Equal("using System;", Encoding.UTF8.GetString(systemSlice));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(systemSlice)), system.NewHash);
        Assert.Equal("using A;", Encoding.UTF8.GetString(aSlice));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(aSlice)), a.NewHash);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepCrlfWithoutFinalNewline_WhenTsMergeRewritesUnterminatedLastLine()
    {
        await SeedAsync("c.ts", "import { Z } from './y';\r\nimport { A } from './x';");

        await EnsureAsync("c.ts", "B", "./x");

        Assert.Equal("import { Z } from './y';\r\nimport { A, B } from './x';", await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldKeepLfWithoutFinalNewline_WhenTsMergeRewritesUnterminatedLastLineInLfFile()
    {
        await SeedAsync("c.ts", "import { Z } from './y';\nimport { A } from './x';");

        await EnsureAsync("c.ts", "B", "./x");

        Assert.Equal("import { Z } from './y';\nimport { A, B } from './x';", await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldBeNoOp_WhenMultiLineNamedImportAlreadyHasBinding()
    {
        const string source = "import {\n  A,\n} from './x';\n";
        await SeedAsync("c.ts", source);

        var outcome = await EnsureAsync("c.ts", "A", "./x");

        Assert.False(outcome.Applied);
        Assert.Equal(source, await ReadAsync("c.ts"));
    }

    [Theory]
    [Trait("BR", "RT-42")]
    [InlineData("import {\n  A,\n} from './x';\n", "import {\n  A,\n  B,\n} from './x';\n")]
    [InlineData("import {\n  A\n} from './x';\n", "import {\n  A,\n  B\n} from './x';\n")]
    public async Task ShouldMergeInsideBraces_WhenMultiLineNamedImportLacksBinding(string source, string expected)
    {
        await SeedAsync("c.ts", source);

        var outcome = await EnsureAsync("c.ts", "B", "./x");

        Assert.True(outcome.Applied);
        Assert.Equal(expected, await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldSeeBindings_WhenImportTypeSpansLines()
    {
        const string source = "import type {\n  A,\n} from './x';\n";
        await SeedAsync("c.ts", source);

        var outcome = await EnsureAsync("c.ts", "A", "./x");

        Assert.False(outcome.Applied);
        Assert.Equal(source, await ReadAsync("c.ts"));
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldUseMajorityLineEnding_WhenFileIsMostlyLfWithOneCrlfLine()
    {
        await SeedAsync("c.cs", "using System;\r\nusing B;\nusing D;\n\nnamespace N;\n");

        await EnsureAsync("c.cs", "C");

        Assert.Equal("using System;\r\nusing B;\nusing C;\nusing D;\n\nnamespace N;\n", await ReadAsync("c.cs"));
    }
}
