using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Harmonisation rc.5, D08: two agents whose names start with the same letter never wear the
/// same avatar.
/// </summary>
public partial class CreateTeamWizardTests
{
    [Fact]
    public void Names_that_share_an_initial_take_two_letters()
    {
        var initials = CreateTeamViewModel.AvatarInitials(["Writer", "Wrangler", "Reviewer", "Analyst"]);

        Assert.Equal("Wi|Wa|R|A", string.Join('|', initials));
    }

    [Fact]
    public void Two_letters_an_accent_apart_take_the_first_letter_where_the_names_differ()
    {
        // UX lot 12 (016): an accented "Re" and a plain "Re" read as one avatar.
        var initials = CreateTeamViewModel.AvatarInitials(["Rédacteur", "Relecteur", "Analyste"]);

        Assert.Equal("Rd|Rl|A", string.Join('|', initials));
    }

    [Fact]
    public void Two_letters_that_differ_stay_the_first_two()
    {
        var initials = CreateTeamViewModel.AvatarInitials(["Writer", "Reader", "Router"]);

        Assert.Equal("W|Re|Ro", string.Join('|', initials));
    }

    [Fact]
    public void Two_word_names_that_share_an_initial_take_their_words_initials()
    {
        var initials = CreateTeamViewModel.AvatarInitials(["Data analyst", "Data engineer", "", ""]);

        Assert.Equal("DA|DE|?|?", string.Join('|', initials));
    }

    [Fact]
    public void Distinct_initials_stay_one_capital()
    {
        var initials = CreateTeamViewModel.AvatarInitials(["writer", "Reviewer"]);

        Assert.Equal("W|R", string.Join('|', initials));
    }
}
