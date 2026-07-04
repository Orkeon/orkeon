using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Tests.Configuration;

public class YamlAnchorPreprocessorTests
{
    [Fact]
    public void Preprocess_NoAnchorsSection_ReturnsInputUnchanged()
    {
        const string yaml = """
            name: my-crew
            agents:
              researcher:
                role: Researcher
                backstory: |
                  You are a researcher.
            """;

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Equal(yaml, result);
    }

    [Fact]
    public void Preprocess_SingleAnchorInLiteralBlock_ExpandsWithIndent()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  persistence: &persistence |",
            "    Your last action must be file_write.",
            "    No call → no output.",
            "",
            "agents:",
            "  test:",
            "    backstory: |",
            "      You are an engineer.",
            "      *persistence",
            "      Continue here.",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.DoesNotContain("anchors:", result);
        Assert.Contains("      Your last action must be file_write.", result);
        Assert.Contains("      No call → no output.", result);
        Assert.DoesNotContain("*persistence", result);
    }

    [Fact]
    public void Preprocess_MultipleAnchors_AllExpandedWithBlankLinesPreserved()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  one: &one |",
            "    First rule.",
            "  two: &two |",
            "    Second rule.",
            "",
            "agents:",
            "  a:",
            "    backstory: |",
            "      *one",
            "",
            "      *two",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Contains("      First rule.", result);
        Assert.Contains("      Second rule.", result);
        Assert.DoesNotContain("*one", result);
        Assert.DoesNotContain("*two", result);
        // Blank line between expansions preserved
        Assert.Contains("First rule.\n\n      Second rule.", result);
    }

    [Fact]
    public void Preprocess_IndentPreservation_AllLinesReIndented()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  block: &block |",
            "    Line A",
            "    Line B",
            "    Line C",
            "",
            "agents:",
            "  x:",
            "    backstory: |",
            "        *block",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Contains("        Line A\n        Line B\n        Line C", result);
    }

    [Fact]
    public void Preprocess_UnknownAlias_ThrowsInvalidOperationException()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  defined: &defined |",
            "    text",
            "",
            "agents:",
            "  x:",
            "    backstory: |",
            "      *missing",
            "");

        var ex = Assert.Throws<InvalidOperationException>(() => YamlAnchorPreprocessor.Preprocess(yaml));
        Assert.Contains("*missing", ex.Message);
    }

    [Fact]
    public void Preprocess_EscapedAlias_StripsBackslashAndKeepsLiteral()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  foo: &foo |",
            "    expansion",
            "",
            "agents:",
            "  x:",
            "    backstory: |",
            "      \\*foo",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Contains("      *foo", result);
        Assert.DoesNotContain("expansion", result);
        Assert.DoesNotContain("\\*foo", result);
    }

    [Fact]
    public void Preprocess_MultipleAliasesOnSameLine_LeftUntouched()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  a: &a |",
            "    AA",
            "  b: &b |",
            "    BB",
            "",
            "agents:",
            "  x:",
            "    backstory: |",
            "      *a *b",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Contains("      *a *b", result);
        Assert.DoesNotContain("AA", result);
        Assert.DoesNotContain("BB", result);
    }

    [Fact]
    public void Preprocess_NonLiteralContextMappingAlias_LeftUntouched()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  role_txt: &role_txt |",
            "    A role description.",
            "",
            "settings:",
            "  manager_agent: *role_txt",
            "agents:",
            "  x:",
            "    backstory: |",
            "      *role_txt",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Contains("manager_agent: *role_txt", result);
        Assert.Contains("      A role description.", result);
    }

    [Fact]
    public void Preprocess_RecursiveAnchor_ThrowsInvalidOperationException()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  base: &base |",
            "    base text",
            "  wrapper: &wrapper |",
            "    before",
            "    *base",
            "    after",
            "",
            "agents:",
            "  x:",
            "    backstory: |",
            "      *wrapper",
            "");

        var ex = Assert.Throws<InvalidOperationException>(() => YamlAnchorPreprocessor.Preprocess(yaml));
        Assert.Contains("Recursive", ex.Message);
    }

    [Fact]
    public void Preprocess_EmptyAnchorsMap_RemovesSectionAndLeavesRest()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "",
            "agents:",
            "  x:",
            "    role: R",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.DoesNotContain("anchors:", result);
        Assert.Contains("agents:", result);
        Assert.Contains("role: R", result);
    }

    [Fact]
    public void Preprocess_R12CrewConfig_ExpandsAllBackstoryAliases()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../../project/experiments/02-CREWS/inngest-js-rt/config.yaml"));

        if (!File.Exists(path))
            return; // test-only file, skip when not present in the current checkout

        var yaml = File.ReadAllText(path);

        var processed = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.DoesNotContain("*fqn_from_index", processed);
        Assert.DoesNotContain("*verbatim_citation", processed);
        Assert.DoesNotContain("*write_persistence", processed);
        Assert.DoesNotContain("*decisive_reasoning", processed);
        Assert.DoesNotContain("*semantic_exploration", processed);
        Assert.DoesNotContain("*exact_numbers", processed);
        Assert.Contains("<fqn_sourcing>", processed);
        Assert.Contains("<verbatim_citation_protocol>", processed);
        // Note: <output_persistence> anchor is defined-but-unused since the migration
        // to Solution A (framework-managed deliverables). It remains in the anchors
        // section for potential future reuse by tasks still in tool_call mode.
        Assert.Contains("<decisive_reasoning>", processed);
        Assert.Contains("<semantic_exploration>", processed);
        Assert.Contains("<exact_numbers>", processed);
    }

    [Fact]
    public void Preprocess_EscapedAliasInsideAnchorValue_NotFlaggedAsRecursive()
    {
        var yaml = string.Join('\n',
            "anchors:",
            "  docs: &docs |",
            "    To write a literal alias, use \\*name.",
            "",
            "agents:",
            "  x:",
            "    backstory: |",
            "      *docs",
            "");

        var result = YamlAnchorPreprocessor.Preprocess(yaml);

        Assert.Contains("      To write a literal alias, use \\*name.", result);
    }
}
