using Orkeon.Scripting.Versioning;

namespace Orkeon.Scripting.Tests.Versioning;

public sealed class VersionDirectiveParserTests
{
    [Fact]
    public void ParseOrDefault_returns_declared_version_when_directive_is_present()
    {
        var src = "/// <reference orkeon-script=\"1.0\" />\n\nconst x = 1;\n";

        Assert.Equal("1.0", VersionDirectiveParser.ParseOrDefault(src));
    }

    [Fact]
    public void ParseOrDefault_returns_default_version_when_directive_is_missing()
    {
        var src = "const x = 1;\n";

        Assert.Equal(VersionDirectiveParser.DefaultVersion, VersionDirectiveParser.ParseOrDefault(src));
    }

    [Fact]
    public void ParseAndValidate_throws_ScriptVersionMismatchError_for_unknown_version()
    {
        var src = "/// <reference orkeon-script=\"2.0\" />\nconst x = 1;\n";

        var ex = Assert.Throws<ScriptVersionMismatchError>(() => VersionDirectiveParser.ParseAndValidate(src));
        Assert.Equal("2.0", ex.DeclaredVersion);
    }

    [Fact]
    public void ParseOrDefault_reads_the_line_comment_form_tsc_accepts()
    {
        // tsc rejects the reference form (TS1084) and then skips its semantic pass; a plain line
        // comment is invisible to it.
        // A version other than the default proves the line is read, not defaulted.
        var src = "// @orkeon-script 9.9\n\nconst x = 1;\n";

        Assert.Equal("9.9", VersionDirectiveParser.ParseOrDefault(src));
    }

    [Fact]
    public void ParseAndValidate_throws_for_an_unknown_version_in_the_line_comment_form()
    {
        var src = "// @orkeon-script 2.0\nconst x = 1;\n";

        Assert.Throws<ScriptVersionMismatchError>(() => VersionDirectiveParser.ParseAndValidate(src));
    }

    [Fact]
    public void ParseOrDefault_prefers_the_reference_form_when_both_are_present()
    {
        var src = "/// <reference orkeon-script=\"1.0\" />\n// @orkeon-script 2.0\nconst x = 1;\n";

        Assert.Equal("1.0", VersionDirectiveParser.ParseOrDefault(src));
    }
}
