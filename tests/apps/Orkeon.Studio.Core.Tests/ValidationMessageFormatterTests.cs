using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

public class ValidationMessageFormatterTests
{
    [Fact]
    public void A_finding_states_its_severity_code_path_and_text()
    {
        var line = ValidationMessageFormatter.Format(
            ValidationMessage.Warning(ValidationCodes.LlmSectionMissing, "No Llm section.", "Llm"));

        Assert.Contains("WARN", line, StringComparison.Ordinal);
        Assert.Contains(ValidationCodes.LlmSectionMissing, line, StringComparison.Ordinal);
        Assert.Contains("Llm", line, StringComparison.Ordinal);
        Assert.Contains("No Llm section.", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ValidationSeverity.Error, "ERROR")]
    [InlineData(ValidationSeverity.Warning, "WARN")]
    [InlineData(ValidationSeverity.Information, "INFO")]
    public void Every_severity_is_spelled_out(ValidationSeverity severity, string expected)
    {
        Assert.Contains(expected, ValidationMessageFormatter.SeverityLabel(severity), StringComparison.Ordinal);
    }

    [Fact]
    public void Severity_labels_are_padded_to_the_same_width_so_a_column_stays_aligned()
    {
        var widths = Enum.GetValues<ValidationSeverity>()
            .Select(severity => ValidationMessageFormatter.SeverityLabel(severity).Length)
            .Distinct();

        Assert.Single(widths);
    }

    [Fact]
    public void A_finding_without_a_path_omits_it_rather_than_showing_a_gap()
    {
        var line = ValidationMessageFormatter.Format(ValidationMessage.Error("E", "boom"));

        Assert.Equal("[ERROR] E — boom", line);
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

        var lines = ValidationMessageFormatter.FormatAll(messages);

        Assert.Contains("boom", lines[0], StringComparison.Ordinal);
        Assert.Contains("careful", lines[1], StringComparison.Ordinal);
        Assert.Contains("note", lines[2], StringComparison.Ordinal);
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

        Assert.Contains(
            "1 error(s), 2 warning(s), 0 note(s)",
            ValidationMessageFormatter.Summarize(messages),
            StringComparison.Ordinal);
        Assert.Contains("no findings", ValidationMessageFormatter.Summarize([]), StringComparison.Ordinal);
    }
}
