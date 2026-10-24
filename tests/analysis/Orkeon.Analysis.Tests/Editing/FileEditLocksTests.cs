using Orkeon.Analysis.Abstractions.Editing;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class FileEditLocksTests
{
    [Fact]
    [Trait("BR", "R3")]
    public void ShouldAcquireAndRelease_WhenPathsAreFree()
    {
        string[] paths = ["/locks-t1/a", "/locks-t1/b"];

        var first = FileEditLocks.TryAcquire(paths, "h1", out var held);
        try
        {
            Assert.True(first);
            Assert.Null(held);
            Assert.False(FileEditLocks.TryAcquire(paths, "h2", out _));
        }
        finally
        {
            FileEditLocks.Release(paths, "h1");
        }

        var again = FileEditLocks.TryAcquire(paths, "h2", out var heldAfter);
        FileEditLocks.Release(paths, "h2");
        Assert.True(again);
        Assert.Null(heldAfter);
    }

    [Fact]
    [Trait("BR", "R3")]
    public void ShouldReportHeldPath_WhenAnotherHolderHoldsIt()
    {
        Assert.True(FileEditLocks.TryAcquire(["/locks-t2/b"], "h1", out _));
        try
        {
            var second = FileEditLocks.TryAcquire(["/locks-t2/a", "/locks-t2/b"], "h2", out var held);
            Assert.False(second);
            Assert.Equal("/locks-t2/b", held);

            // all-or-nothing: the free path of the refused call is not kept
            Assert.True(FileEditLocks.TryAcquire(["/locks-t2/a"], "h3", out _));
            FileEditLocks.Release(["/locks-t2/a"], "h3");

            // a non-holder releases nothing
            FileEditLocks.Release(["/locks-t2/b"], "h2");
            Assert.False(FileEditLocks.TryAcquire(["/locks-t2/b"], "h2", out var stillHeld));
            Assert.Equal("/locks-t2/b", stillHeld);
        }
        finally
        {
            FileEditLocks.Release(["/locks-t2/b"], "h1");
        }
    }
}
