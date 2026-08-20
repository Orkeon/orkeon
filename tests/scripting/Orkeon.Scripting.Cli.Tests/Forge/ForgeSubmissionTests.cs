using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The submission channel (SPEC-ORKEON-FORGE §7.1): the model's tool arguments become the
/// document, schema-validated before the box accepts anything, and a rejection goes back
/// to the model as the tool result — the act loop self-corrects without a user round-trip.
/// </summary>
public class ForgeSubmissionTests
{
    private static Orkeon.Domain.Tools.Protocol.ToolCallRequest Request(Dictionary<string, object?> parameters) =>
        new("brief_submit", parameters);

    [Fact]
    public async Task Valid_arguments_land_in_the_box_as_the_submitted_document()
    {
        var box = new ForgeSubmissionBox();
        var tool = new BriefSubmitTool(box);

        var response = await tool.CallAsync(Request(new Dictionary<string, object?>
        {
            ["goal"] = "Résumer les offres",
            ["acceptance"] = new List<object?>
            {
                new Dictionary<string, object?> { ["id"] = "A1", ["statement"] = "Cite ses sources", ["kind"] = "must" },
            },
        }), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Contains("submitted", (string)response.Result!, StringComparison.OrdinalIgnoreCase);

        var (briefJson, blueprintJson) = box.Take();
        Assert.Null(blueprintJson);
        Assert.True(ForgeBrief.TryParse(briefJson!, out var brief, out _));
        Assert.Equal("Résumer les offres", brief!.Goal);
    }

    [Fact]
    public async Task A_schema_invalid_submission_is_rejected_with_the_errors_and_stays_out_of_the_box()
    {
        var box = new ForgeSubmissionBox();
        var tool = new BriefSubmitTool(box);

        var response = await tool.CallAsync(
            Request(new Dictionary<string, object?> { ["context"] = "no goal, no acceptance" }),
            TestContext.Current.CancellationToken);

        // Success stays true: the REJECTED text is the tool result the model reads.
        Assert.True(response.Success);
        var feedback = (string)response.Result!;
        Assert.StartsWith("REJECTED", feedback, StringComparison.Ordinal);
        Assert.Contains("'goal'", feedback, StringComparison.Ordinal);

        Assert.Equal((null, null), box.Take());
    }

    [Fact]
    public async Task The_blueprint_tool_validates_against_its_own_schema()
    {
        var box = new ForgeSubmissionBox();
        var tool = new BlueprintSubmitTool(box);

        var rejected = await tool.ExecuteAsync("""{ "crew": { "name": "x" } }""", TestContext.Current.CancellationToken);
        Assert.Contains("REJECTED", rejected.Output, StringComparison.Ordinal);

        var accepted = await tool.ExecuteAsync(ForgeDocuments.ValidBlueprint, TestContext.Current.CancellationToken);
        Assert.Contains("submitted", accepted.Output, StringComparison.OrdinalIgnoreCase);

        var (_, blueprintJson) = box.Take();
        Assert.NotNull(blueprintJson);
    }

    [Fact]
    public void The_box_resets_and_takes_atomically()
    {
        var box = new ForgeSubmissionBox();
        box.OfferBrief("""{"goal":"g"}""");
        box.Reset();
        Assert.Equal((null, null), box.Take());

        box.OfferBlueprint("""{"crew":{}}""");
        var first = box.Take();
        Assert.NotNull(first.BlueprintJson);
        Assert.Equal((null, null), box.Take());   // taking empties
    }

    [Fact]
    public void The_usage_tally_sums_prompt_and_completion_tokens()
    {
        var tally = new ForgeUsageTally();
        tally.Record(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 20 });
        tally.Record(new CostUsageEvent { PromptTokens = 50, CompletionTokens = 5 });

        Assert.Equal(175, tally.TotalTokens);
    }
}
