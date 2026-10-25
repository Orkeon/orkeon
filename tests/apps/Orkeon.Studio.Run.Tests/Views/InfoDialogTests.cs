// CS0618: the dialogs under test build a TextView, which Terminal.Gui marks obsolete; the
// production project silences it the same way (Orkeon.Studio.Run.csproj NoWarn).
#pragma warning disable CS0618

using Orkeon.Studio.Run.Views;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>The read-only text panel, built without a terminal.</summary>
public class InfoDialogTests
{
    [Fact]
    public void ShouldShowTheTextReadOnly_WhenBuilt()
    {
        using var dialog = new InfoDialog("Recent launches", "No launch has been recorded yet.");

        var body = ViewTree.One<TextView>(dialog);
        Assert.Equal("Recent launches", dialog.Title);
        Assert.Equal("No launch has been recorded yet.", body.Text);
        Assert.True(body.ReadOnly);
        Assert.True(body.WordWrap);
    }

    [Fact]
    public void ShouldOfferACloseButton_WhenBuilt()
    {
        using var dialog = new InfoDialog("t", "x");

        var close = ViewTree.Button(dialog, "Close");
        ViewTree.Press(close);

        Assert.True(close.IsDefault);
    }
}
