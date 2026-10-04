using System.Reflection;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Common;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-55, decision 5: every code Studio raises before saving or launching has its plain-language
/// explanation (<c>Studio.Diagnostics.Code.&lt;code&gt;</c>) in the English registry — which the parity
/// tests then require of the five <c>.resx</c> —, so a refusal reads in the language of the screen and
/// the English line steps back to the tooltip. The guard reads every constant of Studio.Core whose
/// value starts with <c>STUDIO-</c> or <c>WIN-</c>: today's three classes and any class to come.
/// </summary>
public sealed class DiagnosticCodeOverlayTests
{
    private static IReadOnlyList<string> StudioCoreCodes() =>
    [
        .. typeof(ValidationCodes).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.StartsWith("STUDIO-", StringComparison.Ordinal) || value.StartsWith("WIN-", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static IReadOnlyList<string> Overlays() =>
    [
        .. EnglishStudioStrings.All.Keys
            .Where(key => key.StartsWith(ValidationMessageViewModel.FriendlyKeyPrefix, StringComparison.Ordinal))
            .Select(key => key[ValidationMessageViewModel.FriendlyKeyPrefix.Length..]),
    ];

    /// <summary>The codes without an explanation, then the explanations without a code.</summary>
    private static (IReadOnlyList<string> Unexplained, IReadOnlyList<string> Orphans) Compare(
        IReadOnlyList<string> codes, IReadOnlyList<string> overlays) =>
        ([.. codes.Except(overlays, StringComparer.Ordinal)], [.. overlays.Except(codes, StringComparer.Ordinal)]);

    [Fact]
    public void Every_code_of_studio_core_has_its_explanation_in_the_english_registry()
    {
        var (unexplained, _) = Compare(StudioCoreCodes(), Overlays());

        Assert.True(unexplained.Count == 0, "Codes without a plain-language explanation: " + string.Join(", ", unexplained));
    }

    [Fact]
    public void No_explanation_outlives_its_code()
    {
        var (_, orphans) = Compare(StudioCoreCodes(), Overlays());

        Assert.True(orphans.Count == 0, "Explanations whose code no longer exists: " + string.Join(", ", orphans));
    }

    [Fact]
    public void The_guard_names_a_code_it_cannot_explain_and_an_explanation_left_alone()
    {
        var codes = StudioCoreCodes();
        var overlays = Overlays();

        var (unexplained, orphans) = Compare([.. codes, "STUDIO-WITNESS"], [.. overlays, "STUDIO-GONE"]);

        Assert.Equal(["STUDIO-WITNESS"], unexplained.Where(code => code is "STUDIO-WITNESS"));
        Assert.Contains("STUDIO-GONE", orphans);
        Assert.Contains(ValidationCodes.LlmApiKeyPlaceholder, codes);
        Assert.Contains("STUDIO-TARGET-MISSING", codes);
    }
}
