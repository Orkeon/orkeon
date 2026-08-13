using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Tests.FileSystem;

public class SubPathOverrideTextTests
{
    [Fact]
    public void An_empty_field_parses_to_no_override()
    {
        Assert.True(SubPathOverrideText.TryParse("", out var overrides, out var error));

        Assert.Empty(overrides);
        Assert.Null(error);
    }

    [Fact]
    public void Blank_lines_are_ignored()
    {
        Assert.True(SubPathOverrideText.TryParse("\n  \n docs:ro \n\n", out var overrides, out _));

        var single = Assert.Single(overrides);
        Assert.Equal("docs", single.RelativePath);
        Assert.Equal(MountRights.ReadOnly, single.Rights);
    }

    [Theory]
    [InlineData("ro", MountRights.ReadOnly)]
    [InlineData("rw", MountRights.ReadWrite)]
    [InlineData("RWND", MountRights.ReadWriteNoDelete)]
    public void Every_token_of_the_closed_list_is_accepted(string token, MountRights expected)
    {
        Assert.True(SubPathOverrideText.TryParse($"sub:{token}", out var overrides, out _));

        Assert.Equal(expected, Assert.Single(overrides).Rights);
    }

    [Fact]
    public void The_separator_is_the_last_colon_so_a_drive_qualified_sub_path_survives()
    {
        Assert.True(SubPathOverrideText.TryParse(@"C:\logs:rw", out var overrides, out _));

        var single = Assert.Single(overrides);
        Assert.Equal(@"C:\logs", single.RelativePath);
        Assert.Equal(MountRights.ReadWrite, single.Rights);
    }

    [Fact]
    public void A_line_without_a_separator_fails_the_whole_parse()
    {
        Assert.False(SubPathOverrideText.TryParse("docs:ro\nnonsense", out var overrides, out var error));

        Assert.Empty(overrides);
        Assert.Contains("nonsense", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_rights_token_names_the_ones_that_exist()
    {
        Assert.False(SubPathOverrideText.TryParse("docs:wx", out _, out var error));

        Assert.Contains("wx", error!, StringComparison.Ordinal);
        foreach (var token in MountRightsTokens.Tokens)
            Assert.Contains(token, error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatting_and_parsing_round_trip()
    {
        IReadOnlyList<SubPathRightsOverride> original =
        [
            new("docs", MountRights.ReadOnly),
            new("out", MountRights.ReadWriteNoDelete),
        ];

        Assert.True(SubPathOverrideText.TryParse(SubPathOverrideText.Format(original), out var parsed, out _));

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void A_null_list_formats_to_an_empty_field()
    {
        Assert.Equal(string.Empty, SubPathOverrideText.Format(null));
    }
}
