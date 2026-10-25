using Orkeon.Infrastructure.Session;

namespace Orkeon.Infrastructure.Tests.Session;

/// <summary>The project folder name keeps the last segment of a key, whatever its separator.</summary>
public sealed class ProjectKeyTests
{
    [Theory]
    [Trait("BR", "RT-16")]
    [InlineData("/workspace/project-a")]
    [InlineData(@"C:\work\project-a")]
    [InlineData(@"C:\work/mixed\project-a\")]
    public void ShouldNameTheFolderAfterTheLastSegment_WhateverTheSeparator(string projectKey)
    {
        var slug = ProjectKey.Slug(projectKey);

        Assert.StartsWith("project-a-", slug, StringComparison.Ordinal);
        Assert.Equal("project-a-".Length + 12, slug.Length);
    }
}
