using Orkeon.Studio.Run.Views;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>The "pick one" list, built without a terminal and driven through its buttons.</summary>
public class ChoiceDialogTests
{
    [Fact]
    public void ShouldRejectNullChoices_WhenBuilt()
        => Assert.Throws<ArgumentNullException>(() => new ChoiceDialog("t", "p", null!, "Go"));

    [Fact]
    public void ShouldListTheChoicesUnderThePrompt_WhenBuilt()
    {
        using var dialog = new ChoiceDialog("Which script?", "Pick one:", ["a.ork.ts", "b.ork.ts"], "Run this one");

        Assert.Equal("Which script?", dialog.Title);
        Assert.Equal(["a.ork.ts", "b.ork.ts"], ViewTree.Rows(ViewTree.One<ListView>(dialog)));
        Assert.Contains(ViewTree.All<Label>(dialog), l => l.Text == "Pick one:");
        Assert.Equal(-1, dialog.SelectedIndex);
    }

    [Fact]
    public void ShouldReturnTheHighlightedIndex_WhenAccepted()
    {
        using var dialog = new ChoiceDialog("t", "p", ["a", "b", "c"], "Run this one");
        ViewTree.One<ListView>(dialog).SelectedItem = 2;

        ViewTree.Press(ViewTree.Button(dialog, "Run this one"));

        Assert.Equal(2, dialog.SelectedIndex);
    }

    [Fact]
    public void ShouldReturnMinusOne_WhenAcceptedWithNoChoice()
    {
        using var dialog = new ChoiceDialog("t", "p", [], "Choose");

        ViewTree.Press(ViewTree.Button(dialog, "Choose"));

        Assert.Equal(-1, dialog.SelectedIndex);
    }

    [Fact]
    public void ShouldReturnMinusOne_WhenCancelledWithAChoiceHighlighted()
    {
        using var dialog = new ChoiceDialog("t", "p", ["a", "b", "c"], "Run this one");
        ViewTree.One<ListView>(dialog).SelectedItem = 2;

        ViewTree.Press(ViewTree.Button(dialog, "Cancel"));

        Assert.Equal(-1, dialog.SelectedIndex);
    }
}
