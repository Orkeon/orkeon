using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>A crew file with an invalid decision or when declaration is refused, naming the task key and the part.</summary>
public sealed class DecisionTaskYamlRefusalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Triage = """
  triage:
    decision:
      questions:
        category:
          choice:
            bug: a defect
            billing: an invoice question
        urgency:
          score: [low, medium, high]
        refund_requested:
          noul: the customer asks for a refund
      fallback:
        category: bug
""";

    private static string Crew(string process, string tasks) =>
        "name: support\ngoal: Handle tickets\nprocess: " + process + "\nagents:\n  writer: { role: Writer, goal: Write the answer }\ntasks:\n" + tasks;

    private static string Sibling(string key, string whenBlock) =>
        $"\n  {key}:\n    description: Handle it\n    expectedOutput: An answer\n    agent: writer\n    when:\n{whenBlock}\n";

    private static async Task<string> RefusalAsync(string yaml)
    {
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadFromStringAsync(yaml, Ct));
        return ex.Message;
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenTaskHasAgentAndDecision_NamingKey()
    {
        var yaml = Crew("sequential", Triage.Replace("  triage:\n", "  triage:\n    agent: writer\n"));

        var message = await RefusalAsync(yaml);

        Assert.Contains("'triage'", message);
        Assert.Contains("decision", message);
        Assert.Contains("agent", message);
    }

    [Theory]
    [InlineData("      nosuch.category: [bug]", "nosuch")]
    [InlineData("      triage.nosuch: [bug]", "nosuch")]
    [InlineData("      triage.category: [nosuch]", "nosuch")]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenWhenNamesUnknownTaskQuestionOrOption_NamingKey(string whenBlock, string unknown)
    {
        var message = await RefusalAsync(Crew("sequential", Triage + Sibling("refund", whenBlock)));

        Assert.Contains("'refund'", message);
        Assert.Contains("when", message);
        Assert.Contains(unknown, message);
    }

    [Theory]
    [InlineData("threshold")]
    [InlineData("floor")]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenThresholdOutOfRangeOrFloorOutOfRange_NamingKey(string kind)
    {
        var yaml = kind == "threshold"
            ? Crew("sequential", Triage + Sibling("refund", "      triage.urgency: \">= 2.5\""))
            : Crew("sequential", Triage.Replace("      fallback:", "      min_confidence: 2\n      fallback:"));
        var key = kind == "threshold" ? "'refund'" : "'triage'";
        var part = kind == "threshold" ? "when" : "decision";

        var message = await RefusalAsync(yaml);

        Assert.Contains(key, message);
        Assert.Contains(part, message);
    }

    [Theory]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    [InlineData("autonomous")]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenProcessIsHierarchicalConsensualOrAutonomous_NamingKey(string process)
    {
        var message = await RefusalAsync(Crew(process, Triage));

        Assert.Contains("'triage'", message);
        Assert.Contains("decision", message);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "BR-08")]
    public async Task ShouldRefuseYaml_WhenOtherwiseHasNoSibling_NamingKey()
    {
        var message = await RefusalAsync(Crew("sequential", Triage + Sibling("other", "      triage: otherwise")));

        Assert.Contains("'other'", message);
        Assert.Contains("otherwise", message);
        Assert.Contains("when", message);
    }

    [Theory]
    [InlineData("        nosuch: bug", "nosuch")]
    [InlineData("        category: nosuch", "nosuch")]
    [Trait("BR", "BR-07")]
    [Trait("BR", "UC-04")]
    public async Task ShouldRefuseYaml_WhenFallbackNamesUnknownQuestionOrOption_NamingKey(string fallbackLine, string unknown)
    {
        var yaml = Crew("sequential", Triage.Replace("        category: bug", fallbackLine));

        var message = await RefusalAsync(yaml);

        Assert.Contains("'triage'", message);
        Assert.Contains("decision.fallback", message);
        Assert.Contains(unknown, message);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenWhenNamesTaskByKey_AndDecisionTaskIsUnknown_UsingKeyNotDescription()
    {
        var withDescription = Triage.Replace("  triage:\n", "  triage:\n    description: Decide the category of the incoming support ticket\n");
        var byKey = Crew("sequential", withDescription + Sibling("refund", "      triage.category: [bug]"));
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

        var config = await loader.LoadFromStringAsync(byKey, Ct);

        Assert.Equal("triage", config.Tasks.Single(t => t.Key == "refund").Condition!.DecisionTask);

        var byDescription = Crew("sequential",
            withDescription + Sibling("refund", "      \"Decide the category of the incoming support ticket.category\": [bug]"));
        var message = await RefusalAsync(byDescription);
        Assert.Contains("'refund'", message);
    }

    [Theory]
    [InlineData("      triage: otherwise\n      triage.category: [bug]")]
    [InlineData("      triage.category: [bug]\n      triage: otherwise")]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenWhenMixesOtherwiseWithClauses_NamingKey(string whenBlock)
    {
        // A real sibling clause exists, so the refusal cannot come from the otherwise-without-sibling rule.
        var tasks = Triage + Sibling("escalate", "      triage.category: [billing]") + Sibling("refund", whenBlock);

        var message = await RefusalAsync(Crew("sequential", tasks));

        Assert.Contains("'refund'", message);
        Assert.Contains("when", message);
        Assert.Contains("otherwise", message);
    }

    [Theory]
    [InlineData("      \"triage.category\":")]
    [InlineData("      triage.category:\n        bug: yes")]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenWhenClauseValueIsNotAListOrString_NamingKey(string whenBlock)
    {
        var message = await RefusalAsync(Crew("sequential", Triage + Sibling("refund", whenBlock)));

        Assert.Contains("'refund'", message);
        Assert.Contains("when", message);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenWhenKeyHasTooManySegments_NamingKey()
    {
        var message = await RefusalAsync(Crew("sequential", Triage + Sibling("refund", "      triage.category.bug.x: [bug]")));

        Assert.Contains("'refund'", message);
        Assert.Contains("when", message);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenOptionClauseValueIsAList_NamingKey()
    {
        var message = await RefusalAsync(Crew("sequential", Triage + Sibling("refund", "      \"triage.category.bug\": [billing]")));

        Assert.Contains("'refund'", message);
        Assert.Contains("when", message);
        Assert.Contains("triage.category.bug", message);
    }

    [Theory]
    [InlineData("choice+score")]
    [InlineData("choice+noul")]
    [InlineData("score+noul")]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenQuestionDeclaresMoreThanOneKind_NamingKey(string kinds)
    {
        const string choice = "          choice:\n            bug: a defect\n            billing: an invoice question\n";
        const string score = "          score: [low, medium, high]\n";
        const string noul = "          noul: the customer asks for a refund\n";
        var body = kinds switch { "choice+score" => choice + score, "choice+noul" => choice + noul, _ => score + noul };
        var tasks = "  triage:\n    decision:\n      questions:\n        category:\n" + body;

        var message = await RefusalAsync(Crew("sequential", tasks));

        Assert.Contains("'triage'", message);
        Assert.Contains("decision.questions.category", message);
    }
}
