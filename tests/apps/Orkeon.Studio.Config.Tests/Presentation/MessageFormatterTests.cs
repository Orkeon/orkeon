using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class MessageFormatterTests
{
    [Fact]
    public void A_finding_shows_its_severity_code_and_path()
    {
        var line = MessageFormatter.Format(
            ValidationMessage.Warning(ValidationCodes.LlmSectionMissing, "No Llm section.", "Llm"));

        Assert.Contains("WARN", line);
        Assert.Contains("WIN-01", line);
        Assert.Contains("Llm", line);
        Assert.Contains("No Llm section.", line);
    }

    [Fact]
    public void Findings_are_listed_errors_first()
    {
        IReadOnlyList<ValidationMessage> messages =
        [
            ValidationMessage.Information("I", "note"),
            ValidationMessage.Error("E", "boom"),
            ValidationMessage.Warning("W", "careful"),
        ];

        var lines = MessageFormatter.Format(messages);

        Assert.Contains("boom", lines[0]);
        Assert.Contains("careful", lines[1]);
        Assert.Contains("note", lines[2]);
    }

    [Fact]
    public void The_summary_counts_each_severity()
    {
        IReadOnlyList<ValidationMessage> messages =
        [
            ValidationMessage.Error("E", "boom"),
            ValidationMessage.Warning("W", "careful"),
            ValidationMessage.Warning("W", "careful too"),
        ];

        Assert.Contains("1 error(s), 2 warning(s), 0 note(s)", MessageFormatter.Summarize(messages));
        Assert.Contains("no findings", MessageFormatter.Summarize([]));
    }

    [Fact]
    public void A_doctor_report_shows_each_check_and_how_the_process_ended()
    {
        var report = new DoctorReport
        {
            Run = ProcessRunResult.FromExitCode(0, TimeSpan.FromSeconds(1)),
            Checks =
            [
                new DoctorCheck("llm-reachability", DoctorStatus.Ok, "reachable", "ok"),
                new DoctorCheck("mounts", DoctorStatus.Failure, "none declared", "fail"),
            ],
        };

        var lines = MessageFormatter.Format(report);

        Assert.Contains(lines, line => line.Contains("llm-reachability") && line.Contains("ok"));
        Assert.Contains(lines, line => line.Contains("mounts") && line.Contains("FAIL"));
        Assert.Contains(lines, line => line.Contains("Completed successfully"));
    }

    [Fact]
    public void An_unreadable_doctor_output_is_shown_rather_than_reported_as_an_empty_green_table()
    {
        var report = new DoctorReport
        {
            Run = ProcessRunResult.FromExitCode(1, TimeSpan.FromSeconds(1)),
            RawOutput = "boom",
            ParseError = "orkeon doctor printed malformed JSON",
        };

        var lines = MessageFormatter.Format(report);

        Assert.Contains(lines, line => line.Contains("malformed JSON"));
    }

    [Fact]
    public void An_unknown_check_status_is_shown_as_emitted()
    {
        var report = new DoctorReport
        {
            Run = ProcessRunResult.FromExitCode(0, TimeSpan.Zero),
            Checks = [new DoctorCheck("future-check", DoctorStatus.Unknown, "n/a", "degraded")],
        };

        Assert.Contains(MessageFormatter.Format(report), line => line.Contains("degraded"));
    }
}
