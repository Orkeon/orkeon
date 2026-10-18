using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions.Windows;

/// <summary><see cref="WindowsEnvironmentBlock"/>: sorted without case, NUL-separated, double-NUL ended.</summary>
public sealed class WindowsEnvironmentBlockTests
{
    [Fact]
    public void ShouldSortTheVariablesWithoutCase_AndEndWithTwoNuls()
    {
        var block = WindowsEnvironmentBlock.Build(new Dictionary<string, string>
        {
            ["TEMP"] = @"C:\t",
            ["Path"] = @"C:\Windows",
            ["ALLUSERSPROFILE"] = @"C:\ProgramData",
            ["path_ext"] = "x",
        });

        Assert.Equal("ALLUSERSPROFILE=C:\\ProgramData\0Path=C:\\Windows\0path_ext=x\0TEMP=C:\\t\0\0", block);
    }

    [Fact]
    public void ShouldStillEndWithTwoNuls_WhenEmpty()
        => Assert.Equal("\0\0", WindowsEnvironmentBlock.Build([]));

    [Theory]
    [InlineData("", "v")]
    [InlineData("A=B", "v")]
    [InlineData("A", "v\0w")]
    public void ShouldRefuseAVariableThatWouldCorruptTheBlock(string name, string value)
        => Assert.Throws<ArgumentException>(() => WindowsEnvironmentBlock.Build([new KeyValuePair<string, string>(name, value)]));
}
