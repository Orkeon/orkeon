using Orkeon.Constants.Llm;
using Xunit;

namespace Orkeon.Tests.Shared.Llm;

/// <summary>
/// The shared case table of GAP-53 decision 1: address, expected provider key, expected Ollama
/// root (null when the reading is not Ollama), and whether the address is this machine or its
/// Docker host. Plain data.
/// </summary>
public static class LlmEndpointCases
{
    /// <summary>Gets the table: address, provider key, Ollama root, is local.</summary>
    public static TheoryData<string, string, string?, bool> Rows => new()
    {
        { "http://localhost:11434", LlmProviderKeys.Ollama, "http://localhost:11434", true },
        { "http://localhost:11434/v1", LlmProviderKeys.Ollama, "http://localhost:11434", true },
        { "http://localhost:11434/api", LlmProviderKeys.Ollama, "http://localhost:11434", true },
        { "HTTP://LOCALHOST:11434", LlmProviderKeys.Ollama, "http://localhost:11434", true },
        { "http://127.0.0.1:11434/v1", LlmProviderKeys.Ollama, "http://127.0.0.1:11434", true },
        { "http://[::1]:11434", LlmProviderKeys.Ollama, "http://[::1]:11434", true },
        { "http://host.docker.internal:11434/v1", LlmProviderKeys.Ollama, "http://host.docker.internal:11434", true },
        { "http://gpu-box:11434", LlmProviderKeys.Ollama, "http://gpu-box:11434", false },
        { "http://gpu-box:11434/ollama/v1", LlmProviderKeys.Ollama, "http://gpu-box:11434/ollama", false },

        { "http://model-runner.docker.internal/engines/v1", LlmProviderKeys.DockerModelRunner, null, true },
        { LlmProviderEndpoints.DockerModelRunner, LlmProviderKeys.DockerModelRunner, null, true },
        { "http://gpu-box:12434/v1", LlmProviderKeys.DockerModelRunner, null, false },
        { "http://localhost:12434/engines/llama.cpp/v1", LlmProviderKeys.DockerModelRunner, null, true },

        { "http://localhost:8080", LlmProviderKeys.Custom, null, true },
        { "http://localhost:1234/v1", LlmProviderKeys.Custom, null, true },
        { "http://localhost:8000/v1", LlmProviderKeys.Custom, null, true },
        { "http://localhost:3000/api", LlmProviderKeys.Custom, null, true },
        { "http://localhost:8080/v1", LlmProviderKeys.Custom, null, true },
        { "https://llm.internal.acme.com/v1", LlmProviderKeys.Custom, null, false },
        { "http://mylocalhost.example.com:8080/v1", LlmProviderKeys.Custom, null, false },
        { "https://proxy.example.com/11434/v1", LlmProviderKeys.Custom, null, false },

        { LlmProviderEndpoints.OpenAI, LlmProviderKeys.OpenAI, null, false },
        { LlmProviderEndpoints.DeepSeek, LlmProviderKeys.DeepSeek, null, false },
        { LlmProviderEndpoints.Together, LlmProviderKeys.Together, null, false },
        { LlmProviderEndpoints.Gemini, LlmProviderKeys.Gemini, null, false },
        { LlmProviderEndpoints.Qwen, LlmProviderKeys.Qwen, null, false },
        { LlmProviderEndpoints.Kimi, LlmProviderKeys.Kimi, null, false },
        { LlmProviderEndpoints.HuggingFace, LlmProviderKeys.HuggingFace, null, false },
        { LlmProviderEndpoints.Mistral, LlmProviderKeys.Mistral, null, false },
        { LlmProviderEndpoints.Zai, LlmProviderKeys.Zai, null, false },
        { LlmProviderEndpoints.Grok, LlmProviderKeys.Grok, null, false },
        { LlmProviderEndpoints.MiniMax, LlmProviderKeys.MiniMax, null, false },
        { LlmProviderEndpoints.OpenRouter, LlmProviderKeys.OpenRouter, null, false },
        { LlmProviderEndpoints.Mammouth, LlmProviderKeys.Mammouth, null, false },
        { "https://api.z.ai/api/paas/v4", LlmProviderKeys.Zai, null, false },
        { "https://api.moonshot.cn/v1", LlmProviderKeys.Kimi, null, false },
        { "https://api.minimax.io/v1", LlmProviderKeys.MiniMax, null, false },
        { "https://api.minimaxi.com/v1", LlmProviderKeys.MiniMax, null, false },
        { "https://api.mistral.ai/v1", LlmProviderKeys.Mistral, null, false },
        { "HTTPS://API.OPENAI.COM/v1", LlmProviderKeys.OpenAI, null, false },

        { "https://my-deployment.openai.azure.com/", LlmProviderKeys.AzureOpenAI, null, false },
        { "https://myres.openai.azure.com/", LlmProviderKeys.AzureOpenAI, null, false },
        { "https://myres.cognitiveservices.azure.com/openai", LlmProviderKeys.AzureOpenAI, null, false },

        { LlmProviderEndpoints.Anthropic, LlmProviderKeys.Anthropic, null, false },
        { "https://anthropic.corp.example.com", LlmProviderKeys.Anthropic, null, false },
        { "https://gateway.example.com/anthropic", LlmProviderKeys.Anthropic, null, false },
    };
}
