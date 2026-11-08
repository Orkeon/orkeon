using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

public sealed class TscOutputParserTests
{
    private static string Path(string p) => p.Replace("/tmp/work/m/home/me/proj", "/script", StringComparison.Ordinal);

    private static string Text(string t) => t.Replace("/tmp/work/m/home/me/proj", "/script", StringComparison.Ordinal);

    [Fact]
    public void A_located_error_becomes_a_virtual_path_line_column_and_code()
    {
        var parsed = TscOutputParser.Parse(
            "/tmp/work/m/home/me/proj/main.ork.ts(12,7): error TS2322: Type 'string' is not assignable to type 'number'.\n",
            Path, Text);

        var d = Assert.Single(parsed);
        Assert.Equal("/script/main.ork.ts", d.Path);
        Assert.Equal(12, d.Line);
        Assert.Equal(7, d.Column);
        Assert.Equal("TS2322", d.Code);
        Assert.Equal("Type 'string' is not assignable to type 'number'.", d.Message);
        Assert.Equal("/script/main.ork.ts:12:7 TS2322 Type 'string' is not assignable to type 'number'.", d.ToString());
    }

    [Fact]
    public void No_physical_path_survives_in_a_path_a_message_or_a_continuation_line()
    {
        var parsed = TscOutputParser.Parse(
            "/tmp/work/m/home/me/proj/a.ork.ts(1,1): error TS2345: Argument of type 'import(\"/tmp/work/m/home/me/proj/b.ts\").X' is bad.\r\n" +
            "  Type at /tmp/work/m/home/me/proj/c.ts is wrong.\r\n",
            Path, Text);

        var d = Assert.Single(parsed);
        Assert.DoesNotContain("/tmp/work", d.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/home/me", d.ToString(), StringComparison.Ordinal);
        Assert.Contains("/script/c.ts", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_without_a_file_has_no_path_and_a_foreign_line_is_kept_as_a_message()
    {
        var parsed = TscOutputParser.Parse(
            "error TS5083: Cannot read file '/tmp/work/m/home/me/proj/tsconfig.json'.\nenv: 'node': No such file\n",
            Path, Text);

        Assert.Equal(2, parsed.Count);
        Assert.Null(parsed[0].Path);
        Assert.Equal("TS5083", parsed[0].Code);
        Assert.Equal("TS5083 Cannot read file '/script/tsconfig.json'.", parsed[0].ToString());
        Assert.Equal(string.Empty, parsed[1].Code);
        Assert.Equal("env: 'node': No such file", parsed[1].ToString());
    }

    [Fact]
    public void Empty_output_is_no_diagnostic()
    {
        Assert.Empty(TscOutputParser.Parse(string.Empty, Path, Text));
    }

    [Fact]
    public void Blanking_the_pragmas_keeps_every_line_in_place()
    {
        const string Source = "/// <reference orkeon-script=\"1.0\" />\r\n/// <reference path=\"./.orkeon/orkeon.d.ts\" />\nconst x = 1;\n";

        var blanked = TscTypeChecker.BlankPragmas(Source);

        Assert.Equal("\r\n\nconst x = 1;\n", blanked);
    }
}
