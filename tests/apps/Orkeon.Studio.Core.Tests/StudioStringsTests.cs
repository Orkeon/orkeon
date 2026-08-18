using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Targets;
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
            [StudioStringKeys.PresetNoneTitle] = "Aucun / hors ligne",
            [StudioStringKeys.PresetErrorCustomIncomplete] = "Le preset custom exige URL de base et modèle.",
            [StudioStringKeys.PresetGuidanceApiKeyEnv] = "Clé d'API : export {0}=<votre-clé>",
            [StudioStringKeys.ResolutionStep2Description] = "{0} dans le dossier du crew.",
            [StudioStringKeys.RightsReadOnly] = "Lecture seule",
            [StudioStringKeys.TargetDirectoryRunNotice] = "Dossier multi-fichiers : requiert Orkeon >= {0}.",
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

    // ---- Tranche 2: the Core catalogues resolve through the port ----------

    [Fact]
    public void PresetCatalog_DefaultsToEnglish_AndLocalizes()
    {
        Assert.Equal("None / offline", LlmPresets.Catalog.Single(p => p.Name == LlmPresets.None).Title);
        Assert.Equal(
            "Aucun / hors ligne",
            LlmPresets.CatalogFor(new FrenchStrings()).Single(p => p.Name == LlmPresets.None).Title);
    }

    [Fact]
    public void PresetCatalog_KeepsTheSameNamesAndDefaults_AcrossCultures()
    {
        var english = LlmPresets.Catalog;
        var french = LlmPresets.CatalogFor(new FrenchStrings());

        Assert.Equal(english.Select(p => p.Name), french.Select(p => p.Name));
        Assert.Equal(english.Select(p => p.DefaultBaseUrl), french.Select(p => p.DefaultBaseUrl));
        Assert.Equal(english.Select(p => p.DefaultModel), french.Select(p => p.DefaultModel));
        Assert.Equal(english.Select(p => p.RequiresApiKey), french.Select(p => p.RequiresApiKey));
    }

    [Fact]
    public void TryCreatePlan_LocalizesItsErrors_ButNotItsPlan()
    {
        var ok = LlmPresets.TryCreatePlan(
            LlmPresets.Custom, null, new FrenchStrings(), out var plan, out var error);

        Assert.False(ok);
        Assert.Null(plan);
        Assert.Equal("Le preset custom exige URL de base et modèle.", error);
    }

    [Fact]
    public void Guidance_ResolvesThroughThePort()
    {
        var ok = LlmPresets.TryCreatePlan(LlmPresets.OpenAI, null, out var plan, out _);

        Assert.True(ok);
        var line = Assert.Single(LlmPresets.Guidance(plan!, new FrenchStrings()));
        Assert.Equal("Clé d'API : export ORKEON_Llm__ApiKey=<votre-clé>", line);
    }

    [Fact]
    public void ResolutionChain_LocalizesItsWording_AndKeepsTheFileName()
    {
        var step2 = SettingsLocations.ResolutionChainFor(new FrenchStrings())[1];

        Assert.Equal(2, step2.Order);
        Assert.Equal("appsettings.json dans le dossier du crew.", step2.Description);
    }

    [Fact]
    public void RightsLabels_LocalizeThroughThePort()
    {
        Assert.Equal("Read only", MountRightsTokens.GetLabel(MountRights.ReadOnly));
        Assert.Equal("Lecture seule", MountRightsTokens.GetLabel(MountRights.ReadOnly, new FrenchStrings()));
        Assert.Equal(
            "Lecture seule",
            MountRightsTokens.ChoicesFor(new FrenchStrings())[0].Label);
        // The tokens are the mount-string contract and never localize.
        Assert.Equal(["ro", "rw", "rwnd"], MountRightsTokens.ChoicesFor(new FrenchStrings()).Select(c => c.Token));
    }

    [Fact]
    public void DirectoryRunNotice_LocalizesWithTheMinimumVersion()
    {
        Assert.Equal(
            $"Dossier multi-fichiers : requiert Orkeon >= {RunTargetRequirements.MinimumCliVersion}.",
            RunTargetRequirements.DirectoryRunNoticeFor(new FrenchStrings()));
    }

    [Fact]
    public void EnglishRegistry_ExposesEveryKey_ThroughAll()
    {
        // Spot-check the registry-of-record surface the WPF drift test relies on.
        Assert.True(EnglishStudioStrings.All.Count >= 80);
        Assert.All(EnglishStudioStrings.All, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
            Assert.Equal(entry.Value, EnglishStudioStrings.Instance[entry.Key]);
        });
    }
}
