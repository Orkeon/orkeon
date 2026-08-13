using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// Reading <c>orkeon doctor --json</c>. The CLI's schema is a stable contract
/// (<c>{check, status, detail}</c>), but the parser has to survive a CLI that is one version
/// ahead and a stdout that carries a stray warning line.
/// </summary>
public sealed class DoctorReportParserTests
{
    private const string RealShapedOutput =
        """
        [{"check":"dotnet-runtime","status":"ok","detail":"embedded (self-contained, .NET 10.0.0)"},
         {"check":"appsettings","status":"warn","detail":"no appsettings.json found (env vars only)"},
         {"check":"llm-reachability","status":"fail","detail":"endpoint http://localhost:11434 refused the connection"}]
        """;

    [Fact]
    public void The_documented_schema_is_read()
    {
        Assert.True(DoctorReportParser.TryParse(RealShapedOutput, out var checks, out var error));

        Assert.Null(error);
        Assert.Equal(3, checks.Count);
        Assert.Equal("dotnet-runtime", checks[0].Check);
        Assert.Equal(DoctorStatus.Ok, checks[0].Status);
        Assert.Equal(DoctorStatus.Warning, checks[1].Status);
        Assert.Equal(DoctorStatus.Failure, checks[2].Status);
        Assert.Contains("refused", checks[2].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stray_line_before_the_array_does_not_break_the_parse()
    {
        var output = "warning: settings file not found\n" + RealShapedOutput + "\n";

        Assert.True(DoctorReportParser.TryParse(output, out var checks, out _));
        Assert.Equal(3, checks.Count);
    }

    [Fact]
    public void An_unknown_status_is_surfaced_rather_than_dropped()
    {
        var output = """[{"check":"future-check","status":"degraded","detail":"something new"}]""";

        Assert.True(DoctorReportParser.TryParse(output, out var checks, out _));

        var check = Assert.Single(checks);
        Assert.Equal(DoctorStatus.Unknown, check.Status);
        Assert.Equal("degraded", check.RawStatus);
        Assert.Equal("something new", check.Detail);
    }

    [Fact]
    public void Extra_fields_are_ignored_and_missing_details_default_to_empty()
    {
        var output = """[{"check":"esbuild","status":"ok","duration_ms":12}]""";

        Assert.True(DoctorReportParser.TryParse(output, out var checks, out _));

        var check = Assert.Single(checks);
        Assert.Equal("esbuild", check.Check);
        Assert.Equal("", check.Detail);
    }

    [Fact]
    public void Entries_without_a_check_name_are_skipped()
    {
        var output = """[{"status":"ok"}, 42, {"check":"appsettings","status":"ok","detail":"d"}]""";

        Assert.True(DoctorReportParser.TryParse(output, out var checks, out _));

        Assert.Equal("appsettings", Assert.Single(checks).Check);
    }

    [Fact]
    public void Table_output_is_reported_as_a_parse_error_not_as_an_empty_green_report()
    {
        Assert.False(DoctorReportParser.TryParse("✅ appsettings  /home/me/appsettings.json", out var checks, out var error));

        Assert.Empty(checks);
        Assert.Contains("--json", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_json_is_reported()
    {
        Assert.False(DoctorReportParser.TryParse("""[{"check":"a","status":}]""", out _, out var error));
        Assert.Contains("malformed", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_output_is_reported(string? output)
    {
        Assert.False(DoctorReportParser.TryParse(output, out _, out var error));
        Assert.Contains("no output", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ok", DoctorStatus.Ok)]
    [InlineData("warn", DoctorStatus.Warning)]
    [InlineData("fail", DoctorStatus.Failure)]
    [InlineData("OK", DoctorStatus.Unknown)]
    [InlineData(null, DoctorStatus.Unknown)]
    public void The_status_vocabulary_matches_the_cli(string? raw, DoctorStatus expected)
    {
        Assert.Equal(expected, DoctorReportParser.ParseStatus(raw));
    }
}
