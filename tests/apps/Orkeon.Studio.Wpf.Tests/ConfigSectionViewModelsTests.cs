using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class LlmSectionViewModelTests
{
    private static (LlmSectionViewModel Section, AppSettingsDocument Document, Func<int> Changes) Build(
        string json = "{}")
    {
        var document = AppSettingsDocument.Parse(json);
        var changes = 0;
        var section = new LlmSectionViewModel(() => document, () => changes++);
        return (section, document, () => changes);
    }

    [Fact]
    public void Should_WriteIntoTheDocument_When_AFieldIsEdited()
    {
        var (section, document, changes) = Build();

        section.Model = "gpt-4o-mini";

        Assert.Equal("gpt-4o-mini", document.GetString("Llm:Model"));
        Assert.Equal(1, changes());
    }

    [Fact]
    public void Should_RemoveTheKey_When_AFieldIsCleared()
    {
        var (section, document, _) = Build("""{"Llm":{"Model":"m"}}""");

        section.Model = "   ";

        Assert.Null(document.GetNode("Llm:Model"));
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "openai")]
    [InlineData("https://api.anthropic.com/v1", "anthropic")]
    [InlineData("http://localhost:11434/v1", LlmProviderDetector.Ollama)]
    [InlineData("http://localhost:12434/engines/llama.cpp/v1", LlmProviderDetector.DockerModelRunner)]
    [InlineData("https://my-host.openai.azure.com/", LlmProviderDetector.AzureOpenAI)]
    [InlineData("https://unknown.example.com/v1", LlmProviderDetector.Custom)]
    public void Should_DetectTheProvider_From_TheBaseUrlHost(string baseUrl, string expected)
    {
        var (section, _, _) = Build();

        section.BaseUrl = baseUrl;

        Assert.Equal(expected, section.DetectedProvider);
    }

    [Fact]
    public void Should_ReportNoProvider_When_ThereIsNoBaseUrl()
    {
        var (section, _, _) = Build();

        Assert.Equal(LlmProviderDetector.None, section.DetectedProvider);
    }

    [Fact]
    public void Should_RecommendTheEnvironmentVariable_For_TheApiKey()
    {
        Assert.Equal("ORKEON_Llm__ApiKey", LlmSectionViewModel.ApiKeyEnvironmentVariable);
        Assert.Contains("ORKEON_Llm__ApiKey", LlmSectionViewModel.ApiKeyRecommendation, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_FlagAnInlineKey_When_OneIsStoredInTheFile()
    {
        var (section, _, _) = Build();

        section.ApiKey = "sk-secret";

        Assert.True(section.HasInlineApiKey);
    }

    [Fact]
    public void Should_SeeTheNewFile_When_TheDocumentIsSwapped()
    {
        // The forms reach the document through a delegate, which is what lets "open another file"
        // keep the bindings the view already holds.
        var document = AppSettingsDocument.Parse("""{"Llm":{"Model":"first"}}""");
        var section = new LlmSectionViewModel(() => document, () => { });

        document = AppSettingsDocument.Parse("""{"Llm":{"Model":"second"}}""");

        Assert.Equal("second", section.Model);
    }
}

public sealed class RagSectionViewModelTests
{
    private static RagSectionViewModel Build(AppSettingsDocument document) =>
        new(() => document, () => { });

    [Theory]
    [InlineData("fast")]
    [InlineData("balanced")]
    [InlineData("quality")]
    [InlineData("adaptive")]
    [InlineData("corrective")]
    public void Should_OfferEveryKnownProfile_In_TheClosedList(string profile)
    {
        // The combo box is populated from RagProfilePresets, so it cannot drift from the runtime.
        Assert.Contains(profile, RagSectionViewModel.KnownProfiles);
    }

    [Fact]
    public void Should_AcceptAKnownProfile()
    {
        var section = Build(AppSettingsDocument.CreateEmpty());

        section.Profile = "balanced";

        Assert.True(section.HasValidProfile);
    }

    [Fact]
    public void Should_RejectAnUnknownProfile()
    {
        var section = Build(AppSettingsDocument.CreateEmpty());

        section.Profile = "turbo";

        Assert.False(section.HasValidProfile);
    }

    [Fact]
    public void Should_StayOff_When_OnlyOneHalfOfTheWebFallbackIsEnabled()
    {
        var section = Build(AppSettingsDocument.CreateEmpty());

        section.CorrectiveWebFallbackEnabled = true;

        Assert.False(section.WebFallbackFullyEnabled);
        Assert.Contains("transport", section.WebFallbackStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_TurnOn_When_BothHalvesOfTheWebFallbackAreEnabled()
    {
        var section = Build(AppSettingsDocument.CreateEmpty());

        section.CorrectiveWebFallbackEnabled = true;
        section.WebFallbackEnabled = true;

        Assert.True(section.WebFallbackFullyEnabled);
    }

    [Fact]
    public void Should_WriteTheNestedKeys_When_SwitchesAreToggled()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var section = Build(document);

        section.HybridRetrievalEnabled = true;

        Assert.True(document.GetBoolean("Orkeon:Rag:Retrieval:Hybrid:Enabled"));
    }
}

public sealed class LoggingSectionViewModelTests
{
    [Fact]
    public void Should_LoadEveryCategory_From_TheDocument()
    {
        var document = AppSettingsDocument.Parse(
            """{"Logging":{"LogLevel":{"Default":"Information","Orkeon":"Debug"}}}""");

        var section = new LoggingSectionViewModel(() => document, () => { });

        Assert.Equal(2, section.Categories.Count);
        Assert.Equal("Information", section.DefaultLevel);
    }

    [Fact]
    public void Should_RewriteTheMap_When_ACategoryIsRenamed()
    {
        var document = AppSettingsDocument.Parse("""{"Logging":{"LogLevel":{"Old":"Debug"}}}""");
        var section = new LoggingSectionViewModel(() => document, () => { });

        section.Categories[0].Category = "New";

        Assert.Null(document.GetString("Logging:LogLevel:Old"));
        Assert.Equal("Debug", document.GetString("Logging:LogLevel:New"));
    }

    [Fact]
    public void Should_DropTheCategory_When_ARowIsRemoved()
    {
        var document = AppSettingsDocument.Parse("""{"Logging":{"LogLevel":{"Orkeon":"Debug"}}}""");
        var section = new LoggingSectionViewModel(() => document, () => { });

        section.RemoveCategory(section.Categories[0]);

        Assert.Null(document.GetString("Logging:LogLevel:Orkeon"));
    }

    [Fact]
    public void Should_OfferTheClosedLevelList()
    {
        Assert.Contains("Warning", LoggingSectionViewModel.KnownLevels);
        Assert.Contains("None", LoggingSectionViewModel.KnownLevels);
    }
}

public sealed class PresetSelectionViewModelTests
{
    [Fact]
    public void Should_SeedTheForm_When_APresetIsSelected()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var presets = new PresetSelectionViewModel(() => document, () => { })
        {
            SelectedPreset = PresetSelectionViewModel.Catalog.First(p => p.Name == LlmPresets.DockerModelRunner),
        };

        Assert.Equal(LlmPresets.DockerModelRunnerBaseUrl, presets.BaseUrl);
        Assert.Equal(LlmPresets.DockerModelRunnerDefaultModel, presets.Model);
    }

    [Fact]
    public void Should_WriteTheLlmSection_When_APresetIsApplied()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var applied = 0;
        var presets = new PresetSelectionViewModel(() => document, () => applied++)
        {
            SelectedPreset = PresetSelectionViewModel.Catalog.First(p => p.Name == LlmPresets.Ollama),
        };

        Assert.True(presets.Apply());
        Assert.Equal(1, applied);
        Assert.NotNull(document.Llm.Model);
        Assert.NotNull(document.Llm.BaseUrl);
    }

    [Fact]
    public void Should_RefuseAndExplain_When_TheCustomPresetLacksItsFields()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var presets = new PresetSelectionViewModel(() => document, () => { })
        {
            SelectedPreset = PresetSelectionViewModel.Catalog.First(p => p.Name == LlmPresets.Custom),
        };
        presets.BaseUrl = null;
        presets.Model = null;

        Assert.False(presets.Apply());
        Assert.NotNull(presets.ErrorMessage);
        Assert.False(document.Llm.Exists);
    }

    [Fact]
    public void Should_SurfaceTheApiKeyGuidance_When_TheKeyGoesToTheEnvironment()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var presets = new PresetSelectionViewModel(() => document, () => { })
        {
            SelectedPreset = PresetSelectionViewModel.Catalog.First(p => p.Name == LlmPresets.OpenAI),
        };
        presets.ApiKey = null;

        presets.Apply();

        Assert.Contains(presets.Guidance, line => line.Contains("ORKEON_Llm__ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_RemoveTheLlmSection_When_TheNonePresetIsApplied()
    {
        var document = AppSettingsDocument.Parse("""{"Llm":{"Model":"m"}}""");
        var presets = new PresetSelectionViewModel(() => document, () => { })
        {
            SelectedPreset = PresetSelectionViewModel.Catalog.First(p => p.Name == LlmPresets.None),
        };

        presets.Apply();

        Assert.False(document.Llm.Exists);
    }
}
