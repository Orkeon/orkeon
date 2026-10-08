using System.Xml.Linq;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A key's sentence in the settings catalogue is the summary of the property it is read into, taken
/// from the XML documentation the build writes: one line, code in backticks, and nothing a reader of
/// the settings cannot look up.
/// </summary>
public sealed class SettingsDocumentationTests
{
    private static readonly SettingsDocumentation s_documentation = SettingsDocumentation.From(
    [
        XDocument.Parse($"""
            <doc>
              <members>
                <member name="P:{typeof(Documented).FullName!.Replace('+', '.')}.Plain">
                  <summary>
                  Maximum requests per minute of <c>one</c> agent — see <see cref="P:Some.Namespace.Options.QueueLimit"/>,
                  never <see langword="null"/> (GAP-40).
                  <para>Checked when a host starts (<see cref="M:Some.Type.Problem(System.String)"/>, GAP-08) and again later.</para>
                  </summary>
                </member>
                <member name="P:{typeof(Documented).FullName!.Replace('+', '.')}.Standard">
                  <summary>Encrypted at rest (AES-256), as the e-mail decision says (ADR-012).</summary>
                </member>
                <member name="P:{typeof(Documented).FullName!.Replace('+', '.')}.Accessed">
                  <summary>
                  Gets or sets a value indicating whether the cap applies: over <see cref="P:{typeof(Documented).FullName!.Replace('+', '.')}.Plain"/>
                  requests, <see cref="F:{typeof(Mode).FullName!.Replace('+', '.')}.Strict"/> refuses. Defaults to
                  <see cref="F:{typeof(Documented).FullName!.Replace('+', '.')}.DefaultLimit"/>.
                  </summary>
                </member>
                <member name="T:{typeof(Documented).FullName!.Replace('+', '.')}">
                  <summary>The tree of the pipeline (v2, plan §8.1), bound from <c>Orkeon:Rag</c>.</summary>
                </member>
              </members>
            </doc>
            """),
    ]);

    [Fact]
    public void A_summary_is_one_line_with_code_in_backticks_and_references_by_their_short_name() =>
        Assert.Equal(
            "Maximum requests per minute of `one` agent — see `Options.QueueLimit`, never `null`. Checked when a host starts and again later.",
            s_documentation.Of(typeof(Documented).GetProperty(nameof(Documented.Plain))!));

    [Fact]
    public void A_bracket_that_names_a_work_item_or_a_plan_is_left_out_and_a_standard_stays()
    {
        Assert.Equal(
            "Encrypted at rest (AES-256), as the e-mail decision says (ADR-012).",
            s_documentation.Of(typeof(Documented).GetProperty(nameof(Documented.Standard))!));
        Assert.Equal("The tree of the pipeline, bound from `Orkeon:Rag`.", s_documentation.Of(typeof(Documented)));
    }

    [Fact]
    public void A_summary_reads_as_a_key_not_as_a_property() =>
        Assert.Equal(
            "Whether the cap applies: over `Plain` requests, `Strict` refuses. Defaults to `7`.",
            s_documentation.Of(typeof(Documented).GetProperty(nameof(Documented.Accessed))!));

    [Fact]
    public void A_property_takes_the_sentence_of_the_type_it_inherits_it_from() =>
        Assert.StartsWith(
            "Maximum requests",
            s_documentation.Of(typeof(Inheriting).GetProperty(nameof(Documented.Plain))!),
            StringComparison.Ordinal);

    [Fact]
    public void A_member_without_a_comment_has_no_sentence()
    {
        Assert.Equal(string.Empty, s_documentation.Of(typeof(Documented).GetProperty(nameof(Documented.Silent))!));
        Assert.Equal(string.Empty, SettingsDocumentation.Empty.Of(typeof(Documented)));
    }

    private class Documented
    {
        public int Plain { get; set; }

        public int Standard { get; set; }

        public int Silent { get; set; }

        public const int DefaultLimit = 7;

        public Mode Accessed { get; set; }
    }

    private enum Mode
    {
        Lenient,
        Strict,
    }

    private sealed class Inheriting : Documented;
}
