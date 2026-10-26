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
        var initials = CreateTeamViewModel.AvatarInitials(["Rédacteur", "Relecteur", "Analyste"]);

        Assert.Equal("Ré|Re|A", string.Join('|', initials));
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
