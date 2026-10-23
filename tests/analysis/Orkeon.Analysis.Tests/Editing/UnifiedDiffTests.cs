using Orkeon.Analysis.Core.Editing;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class UnifiedDiffTests
{
    private const string Before = "l1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nl9\nl10\n";

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldWriteOneHunkWithThreeContextLines_WhenOneLineChanged()
    {
        var after = Before.Replace("l5\n", "five\n", StringComparison.Ordinal);

        var diff = UnifiedDiff.Write("src/a.txt", Before, after);

        Assert.Equal(
            "--- a/src/a.txt\n+++ b/src/a.txt\n@@ -2,7 +2,7 @@\n l2\n l3\n l4\n-l5\n+five\n l6\n l7\n l8\n",
            diff);
        Assert.Equal("", UnifiedDiff.Write("src/a.txt", Before, Before));
    }
}
