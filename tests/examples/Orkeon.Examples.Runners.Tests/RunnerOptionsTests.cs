using Orkeon.Hosting;

namespace Orkeon.Examples.Runners.Tests;

public class RunnerOptionsTests
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private static TestOptions WithVars(params string[] vars) =>
        new() { Variables = vars };

    [Fact]
    public void ParsedVariables_empty_when_no_var_provided()
    {
        var opts = new TestOptions();
        Assert.Empty(opts.ParseVariables());
    }

    [Fact]
    public void ParsedVariables_single_var_parsed()
    {
        var opts = WithVars("foo=bar");
        var dict = opts.ParseVariables();
        Assert.Single(dict);
        Assert.Equal("bar", dict["foo"]);
    }

    [Fact]
    public void ParsedVariables_multiple_vars_parsed()
    {
        var opts = WithVars("a=1", "b=2", "c=3");
        var dict = opts.ParseVariables();
        Assert.Equal(3, dict.Count);
        Assert.Equal("1", dict["a"]);
        Assert.Equal("2", dict["b"]);
        Assert.Equal("3", dict["c"]);
    }

    [Fact]
    public void ParsedVariables_value_with_equals_sign_kept()
    {
        var opts = WithVars("url=https://x?q=v&w=z");
        Assert.Equal("https://x?q=v&w=z", opts.ParseVariables()["url"]);
    }

    [Fact]
    public void ParsedVariables_value_with_spaces_kept()
    {
        var opts = WithVars("msg=hello world");
        Assert.Equal("hello world", opts.ParseVariables()["msg"]);
    }

    [Fact]
    public void ParsedVariables_empty_value_allowed()
    {
        var opts = WithVars("flag=");
        Assert.Equal(string.Empty, opts.ParseVariables()["flag"]);
    }

    [Fact]
    public void ParsedVariables_throws_on_missing_equals()
    {
        var opts = WithVars("foo");
        Assert.Throws<FormatException>(() => opts.ParseVariables());
    }

    [Fact]
    public void ParsedVariables_throws_on_empty_key()
    {
        var opts = WithVars("=bar");
        Assert.Throws<FormatException>(() => opts.ParseVariables());
    }

    [Fact]
    public void ParsedVariables_throws_on_whitespace_key()
    {
        var opts = WithVars("  =bar");
        Assert.Throws<FormatException>(() => opts.ParseVariables());
    }
}
