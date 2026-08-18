using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-11 tranche 1: the Core formatters resolve their fabricated strings
/// through the localization port — English by default, any culture via the
/// front-end's bridge — and the CLI verdict words stay untranslated.
/// </summary>
public class StudioStringsTests
{
    /// <summary>Hand-written French port, the shape a front-end bridge produces.</summary>
    private sealed class FrenchStrings : IStudioStrings
    {
        private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
        {
            [StudioStringKeys.ValidationNoFindings] = "Validation : aucun constat.",
            [StudioStringKeys.ValidationSummary] = "Validation : {0} erreur(s), {1} avertissement(s), {2} note(s).",
            [StudioStringKeys.LaunchNothingRan] = "Rien n'a été exécuté — {0}",
            [StudioStringKeys.LaunchExitCode] = "Code de sortie {0} — {1}",
        };

        public string this[string key] => Table.GetValueOrDefault(key, key);

        public event EventHandler? CultureChanged { add { } remove { } }
    }

    [Fact]
    public void Summarize_DefaultsToEnglish()
    {
        var line = ValidationMessageFormatter.Summarize([]);

        Assert.Equal("Validation: no findings.", line);
    }

    [Fact]
    public void Summarize_UsesTheProvidedCulturePort()
    {
        var messages = new[]
        {
            ValidationMessage.Error("ORK001", "missing", "Llm.Model"),
            ValidationMessage.Warning("ORK002", "empty", "Llm.ApiKey"),
        };

        var line = ValidationMessageFormatter.Summarize(messages, new FrenchStrings());

        Assert.Equal("Validation : 1 erreur(s), 1 avertissement(s), 0 note(s).", line);
    }

    [Fact]
    public void DescribeRun_UsesTheProvidedCulturePort()
    {
        var result = new ProcessRunResult
        {
            ExitCode = 0,
            RawExitCode = 0,
            Outcome = RunOutcome.Success,
            Termination = ProcessTerminationMode.Exited,
            Description = "success",
        };

        var line = LaunchOutcomeFormatter.DescribeRun(result, new FrenchStrings());

        Assert.Equal("Code de sortie 0 — success", line);
    }

    [Fact]
    public void DescribeValidation_KeepsTheCliVerdictUntranslated()
    {
        var result = new ProcessRunResult
        {
            ExitCode = 0,
            RawExitCode = 0,
            Outcome = RunOutcome.Success,
            Termination = ProcessTerminationMode.Exited,
            Description = "success",
        };

        var line = LaunchOutcomeFormatter.DescribeValidation(result, new FrenchStrings());

        // The verdict is the CLI contract; only the sentence around it localizes.
        Assert.StartsWith("VALIDATION OK — ", line, StringComparison.Ordinal);
        Assert.Contains("Code de sortie 0", line, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishDefaults_ResolveEveryDeclaredKey()
    {
        var strings = EnglishStudioStrings.Instance;

        foreach (var key in new[]
        {
            StudioStringKeys.ValidationNoFindings,
            StudioStringKeys.ValidationSummary,
            StudioStringKeys.LaunchNothingRan,
            StudioStringKeys.LaunchExitCode,
        })
        {
            Assert.NotEqual(key, strings[key]); // an unresolved key echoes itself
        }
    }

    [Fact]
    public void UnknownKey_EchoesItself_InsteadOfThrowing()
    {
        Assert.Equal("Nope_Key", EnglishStudioStrings.Instance["Nope_Key"]);
    }
}
