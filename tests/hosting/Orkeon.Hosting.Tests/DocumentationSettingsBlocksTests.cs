using System.Text.RegularExpressions;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A <c>json</c> block of the documentation that writes a section Orkeon reads writes only its keys:
/// a reader copies a block into a settings file, and the host that reads the file refuses what the
/// page invented. A block that is no settings text — a crew, an event, a tool call — is not judged;
/// one that shows on purpose what a host refuses says so on the line above it
/// (<see cref="DocumentationPages.Exclusion"/>). English and French alike.
/// </summary>
public sealed partial class DocumentationSettingsBlocksTests
{
    private static SettingsFileGuard Guard => SettingsFileGuard.Complete;

    [Fact]
    public void A_json_block_that_writes_a_known_section_writes_only_its_keys()
    {
        var problems = new List<string>();
        var judged = 0;
        foreach (var page in DocumentationPages.All())
        {
            foreach (var block in DocumentationPages.JsonBlocks(DocumentationPages.Text(page)))
            {
                if (block.Excluded)
                    continue;

                using var settings = SettingsFileGuard.Read(block.Text);
                if (settings is null)
                {
                    if (NamesAKnownSection(block.Text) is { } section)
                    {
                        problems.Add(
                            $"{page}:{block.Line}: the block writes {section} and cannot be read as JSON, so nothing holds it to the code. " +
                            $"Complete it, or put {DocumentationPages.Exclusion} on the line above its fence.");
                    }

                    continue;
                }

                if (!Guard.WritesAKnownSection(settings.RootElement))
                    continue;

                judged++;
                problems.AddRange(Guard.Problems(settings.RootElement, openRoot: true).Select(problem => $"{page}:{block.Line}: {problem}"));
            }
        }

        Assert.True(
            problems.Count == 0,
            "A json block of the documentation writes what no host reads (`orkeon settings <section>` lists the keys of a section):\n"
            + string.Join("\n", problems));
        // A pattern that stops matching would turn this control into a silent pass.
        Assert.True(judged >= 30, $"{judged} settings blocks were judged.");
    }

    [Fact]
    public void A_block_is_found_with_its_line_and_the_mark_that_excludes_it()
    {
        const string Page = "# Title\n\n```json\n{ \"RateLimiting\": { \"QueueLimit\": 5 } }\n```\n\ntext\n\n" +
                            DocumentationPages.Exclusion + "\n\n   ```jsonc\n   { \"Llm\": { \"Provider\": \"x\" } }\n   ```\n\n```yaml\nllm: x\n```\n";

        var blocks = DocumentationPages.JsonBlocks(Page).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.Equal((4, false), (blocks[0].Line, blocks[0].Excluded));
        Assert.Equal((12, true), (blocks[1].Line, blocks[1].Excluded));
        Assert.Contains("\"Provider\"", blocks[1].Text, StringComparison.Ordinal);
    }

    /// <summary>The first root section of the catalogue a text that is no JSON still writes as a member, or null.</summary>
    private static string? NamesAKnownSection(string text) =>
        Member().Matches(text)
            .Select(member => member.Groups["name"].Value)
            .FirstOrDefault(name => Guard.RootNames.Contains(name, StringComparer.Ordinal));

    [GeneratedRegex("\"(?<name>[A-Za-z_][A-Za-z0-9_]*)\"\\s*:\\s*[{\\[]")]
    private static partial Regex Member();
}
