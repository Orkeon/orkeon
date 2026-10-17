using Orkeon.Studio.Core.Text;

namespace Orkeon.Studio.Core.Tests.Text;

/// <summary>
/// The one-line path of a folder row: cut in the middle so the folder name at its end stays
/// readable. Widths are measured here in characters, one unit each.
/// </summary>
public sealed class MiddleEllipsisTests
{
    private const string Path = @"C:\Users\user\Orkeon\monitoring-notes\input";

    private static double Chars(string text) => text.Length;

    [Fact]
    public void A_text_that_fits_is_left_whole()
    {
        Assert.Equal(Path, MiddleEllipsis.Fit(Path, Chars, Path.Length));
    }

    [Fact]
    public void A_text_too_long_keeps_its_start_and_its_end()
    {
        var fitted = MiddleEllipsis.Fit(Path, Chars, 21);

        Assert.Equal(21, fitted.Length);
        Assert.StartsWith(@"C:\Users\u", fitted, StringComparison.Ordinal);
        Assert.EndsWith(@"tes\input", fitted, StringComparison.Ordinal);
        Assert.Contains(MiddleEllipsis.Ellipsis, fitted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_longest_form_that_fits_is_chosen()
    {
        for (var width = 1; width < Path.Length; width++)
            Assert.Equal(width, MiddleEllipsis.Fit(Path, Chars, width).Length);
    }

    [Fact]
    public void No_room_at_all_leaves_the_ellipsis_alone()
    {
        Assert.Equal(MiddleEllipsis.Ellipsis, MiddleEllipsis.Fit(Path, Chars, 0));
    }

    [Fact]
    public void An_empty_text_stays_empty()
    {
        Assert.Equal("", MiddleEllipsis.Fit("", Chars, 0));
    }
}
