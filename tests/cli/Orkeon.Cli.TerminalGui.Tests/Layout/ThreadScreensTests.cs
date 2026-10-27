using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class ThreadScreensTests
{
    private static ThreadScreens Create() => new(100, _ => { });

    [Fact]
    [Trait("BR", "BR-02")]
    public void ShouldAppendToTaskScreen_WhenWrittenUnderTaskThread()
    {
        var screens = Create();

        screens.For("T1").AppendLines(["from task"]);

        Assert.Equal("from task", screens.For("T1").TranscriptText);
        Assert.Equal(string.Empty, screens.Main.TranscriptText);
    }

    [Fact]
    [Trait("BR", "BR-01")]
    public void ShouldOpenScreen_WhenTaskWritesBeforeItsRowAppears()
    {
        var screens = Create();

        var screen = screens.For("T9");
        screen.AppendLines(["early line"]);

        Assert.Contains("T9", screens.Tickets);
        Assert.NotSame(screens.Main, screen);
        Assert.Same(screen, screens.For("T9"));
        Assert.Equal("early line", screen.TranscriptText);
    }

    [Fact]
    [Trait("BR", "BR-08")] // A1
    public void ShouldDiscardNeverListedScreen_WhenItOutlivesOnePollUnlisted()
    {
        var screens = Create();
        screens.For("T9").AppendLines(["early line"]);

        Assert.False(screens.DiscardAbsent([]));
        Assert.Contains("T9", screens.Tickets);

        Assert.False(screens.DiscardAbsent([]));
        Assert.DoesNotContain("T9", screens.Tickets);
    }

    [Fact]
    [Trait("BR", "BR-09")]
    public void ShouldKeepEveryLineInOrder_WhenScreenIsHidden()
    {
        var screens = Create();
        screens.For("T1").AppendLines(["first"]);
        Assert.True(screens.Show("T1"));
        Assert.True(screens.Show(null));

        screens.For("T1").AppendLines(["second"]);
        screens.For("T1").AppendLines(["third"]);
        screens.For("T1").AppendLines(["fourth"]);
        Assert.True(screens.Show("T1"));

        Assert.Same(screens.For("T1"), screens.Displayed);
        Assert.Equal("first\nsecond\nthird\nfourth", screens.Displayed.TranscriptText);
    }
}
