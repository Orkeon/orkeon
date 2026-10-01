using System.Globalization;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// STUDIO-43: the probe's verdict is described in the interface's language — the step, the
/// URL, the time waited and the cause — rather than as a hard-coded English line.
/// </summary>
public sealed class LlmProbeTextTests
{
    /// <summary>A port that answers every key in a recognisable "French" form.</summary>
    private sealed class PseudoFrench : IStudioStrings
    {
        public string this[string key] => key switch
        {
            StudioStringKeys.ProbeFailedAtStage => "Échec à l'étape {0} ({1}, après {2} s) : {3}",
            StudioStringKeys.ProbeStageCompletion => "requête d'essai",
            StudioStringKeys.ProbeTimeout => "pas de réponse en {0} s.",
            _ => EnglishStudioStrings.Instance[key],
        };

        public event EventHandler? CultureChanged { add { } remove { } }
    }

    [Fact]
    public void A_failure_is_described_in_the_interface_language_with_its_step_url_and_time()
    {
        var result = new LlmProbeResult
        {
            Succeeded = false,
            Stage = LlmProbeStage.Completion,
            Failure = LlmProbeFailure.Timeout,
            Url = "https://api.z.ai/api/paas/v4/chat/completions",
            Elapsed = TimeSpan.FromSeconds(30.04),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var text = LlmProbeText.Describe(result, new PseudoFrench(), CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal(
            "Échec à l'étape requête d'essai (https://api.z.ai/api/paas/v4/chat/completions, après 30,0 s) : pas de réponse en 30 s.",
            text);
    }

    [Fact]
    public void The_english_message_is_the_default_description()
    {
        var result = LlmProbeResult.Reachable(3);

        Assert.Equal("Endpoint reachable — 3 model(s).", result.Message);
        Assert.Equal(result.Message, LlmProbeText.Describe(result, EnglishStudioStrings.Instance, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_passed_completion_names_the_model_that_answered()
    {
        var result = new LlmProbeResult
        {
            Succeeded = true,
            Stage = LlmProbeStage.Completion,
            Model = "glm-5.2",
            ModelCount = 4,
            Elapsed = TimeSpan.FromSeconds(1.2),
        };

        Assert.Contains("glm-5.2", result.Message, StringComparison.Ordinal);
        Assert.Contains("1.2 s", result.Message, StringComparison.Ordinal);
    }
}
