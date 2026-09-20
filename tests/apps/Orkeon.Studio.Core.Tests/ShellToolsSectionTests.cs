using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-21 — the <c>Orkeon:Tools:Shell</c> view never writes the empty array the runtime
/// reads as "block every command".
/// </summary>
public sealed class ShellToolsSectionTests
{
    [Fact]
    public void The_three_fields_round_trip_on_the_documented_keys()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.ShellTools.AllowInterpreters = true;
        document.ShellTools.ExtraAllowedCommands = ["git", " dotnet "];
        document.ShellTools.AllowedCommands = ["ls"];

        var reparsed = AppSettingsDocument.Parse(document.ToJson());
        Assert.True(reparsed.GetBoolean("Orkeon:Tools:Shell:AllowInterpreters"));
        Assert.Equal(["git", "dotnet"], reparsed.ShellTools.ExtraAllowedCommands);
        Assert.Equal(["ls"], reparsed.ShellTools.AllowedCommands);
        Assert.True(reparsed.ShellTools.Exists);
    }

    [Fact]
    public void An_empty_or_blank_list_removes_the_key_instead_of_writing_an_empty_array()
    {
        var document = AppSettingsDocument.Parse("""{ "Orkeon": { "Tools": { "Shell": { "AllowedCommands": ["ls"] } } } }""");

        document.ShellTools.AllowedCommands = [];
        document.ShellTools.ExtraAllowedCommands = ["", "   "];

        Assert.Null(document.ShellTools.AllowedCommands);
        Assert.Null(document.ShellTools.ExtraAllowedCommands);
        Assert.DoesNotContain("AllowedCommands", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Absent_lists_read_as_null_not_as_empty()
    {
        var document = AppSettingsDocument.CreateEmpty();

        Assert.Null(document.ShellTools.AllowedCommands);
        Assert.Null(document.ShellTools.ExtraAllowedCommands);
        Assert.Null(document.ShellTools.AllowInterpreters);
        Assert.False(document.ShellTools.Exists);
    }
}
