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
}
