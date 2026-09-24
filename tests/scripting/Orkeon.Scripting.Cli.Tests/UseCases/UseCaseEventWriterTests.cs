using Orkeon.Scripting.Cli.Commands.UseCases;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// The lines Studio reads (STUDIO-38 D-06, D-07) are a wire contract, like the run and forge
/// streams: this golden test pins their serialized form. A failure here means the protocol
/// changed — update the reader in Studio, or fix the regression.
/// </summary>
public sealed class UseCaseEventWriterTests
{
    [Fact]
    public async Task The_search_answer_is_the_pinned_golden_form()
    {
        using var engine = UseCaseFixtures.Engine();
        var answer = await engine.SearchAsync(
            new UseCaseQuery { Text = "résumé mails", Language = "fr", Top = 1 }, TestContext.Current.CancellationToken);
        var output = new StringWriter();
        var writer = new UseCaseEventWriter(output, new FakeOrkeonClock());

        writer.Results(answer, correlationId: "q7");
        writer.Error(UseCaseErrorCodes.QueryInvalid, "A query needs a text.", recoverable: true, correlationId: "q8");

        var score = answer.Matches[0].Score.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var expected = string.Join('\n',
            "{\"v\":2,\"seq\":1,\"ts\":\"2026-08-19T12:00:00Z\",\"kind\":\"usecases.results\",\"correlationId\":\"q7\","
                + "\"query\":\"résumé mails\",\"lang\":\"fr\",\"langSource\":\"option\",\"mode\":\"bm25\","
                + "\"results\":[{\"rank\":1,\"id\":\"01-daily-mail-digest\",\"score\":" + score + ",\"reason\":\"terms\","
                + "\"terms\":[\"resume\",\"mails\"],\"title\":\"Résumé quotidien des e-mails\"}]}",
            "{\"v\":2,\"seq\":2,\"ts\":\"2026-08-19T12:00:01Z\",\"kind\":\"error\",\"correlationId\":\"q8\","
                + "\"code\":\"USECASES-QUERY-INVALID\",\"message\":\"A query needs a text.\",\"recoverable\":true}",
            "");

        Assert.Equal(expected, output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_sheet_line_is_flat_and_carries_the_manifest_field_names()
    {
        var catalog = UseCaseFixtures.Catalog();
        var output = new StringWriter();
        var writer = new UseCaseEventWriter(output, new FakeOrkeonClock());

        writer.Sheet(catalog.Get(UseCaseFixtures.FraudAlerts), [], crew: null);

        var line = System.Text.Json.JsonElement.Parse(output.ToString());
        Assert.Equal("usecases.sheet", line.GetProperty("kind").GetString());
        Assert.Equal("03-fraud-alerts", line.GetProperty("id").GetString());
        Assert.Equal("03-finance-trading", line.GetProperty("category").GetString());
        Assert.Equal(3, line.GetProperty("number").GetInt32());
        Assert.True(line.GetProperty("requiresNetwork").GetBoolean());
        Assert.Equal("SERPER_API_KEY", line.GetProperty("requiresKeys")[0].GetString());
        Assert.Equal("银行欺诈检测", line.GetProperty("title").GetProperty("zh-Hans").GetString());
        Assert.Equal(0, line.GetProperty("files").GetArrayLength());
        Assert.Contains("银行欺诈检测", output.ToString(), StringComparison.Ordinal);
    }
}
