namespace Orkeon.Tests.Shared.Constants;

/// <summary>LLM constants for tests.</summary>
public static class TestLlmConstants
{
    // --- Model names ---
    /// <summary>
    /// The platform's default model (<c>LlmDefaults.DefaultModelName</c>).
    /// Use it when the assertion is about the default value itself, not about a
    /// specific identifier — the two were conflated before LLM-01, where the default
    /// moved from <c>gpt-4</c> to <c>gpt-5.6-sol</c>.
    /// </summary>
    public const string ModelDefault = "gpt-5.6-sol";
    public const string ModelGpt4 = "gpt-4";
    public const string ModelGpt4o = "gpt-4o";
    public const string ModelGpt35Turbo = "gpt-3.5-turbo";
    public const string ModelGpt4Turbo = "gpt-4-turbo";
    public const string ModelGpt4oMini = "gpt-4o-mini";
    public const string ModelClaude3 = "claude-3";
    public const string ModelClaude3Opus = "claude-3-opus-20240229";
    public const string ModelLlama2 = "llama2";
    public const string ModelMistral = "mistral";
    public const string TestModelName = "test-model";
    public const string CustomModelName = "custom-model";

    // --- Providers ---
    public const string ProviderOpenAI = "openai";
    public const string ProviderOllama = "ollama";
    public const string ProviderAnthropic = "anthropic";
    public const string ProviderMistral = "mistral";

    // --- Endpoints ---
    public const string EndpointMistral = "https://api.mistral.ai/v1";

    // --- Default configuration ---
    public const double DefaultTemperature = 0.7;
    public const double DefaultTopP = 0.95;
    public const int DefaultMaxTokens = 4096;
    public const int HighMaxTokens = 8192;
    public const int DefaultTimeoutSeconds = 30;

    // --- Test API keys ---
    public const string TestApiKey = "test-api-key";
    public const string CustomApiKey = "custom-api-key";
}
