namespace Orkeon.Infrastructure.Constants.Llm;

/// <summary>
/// Defaults for Docker Model Runner — the local llama.cpp engine Docker Desktop serves over
/// an OpenAI-compatible API. It is not an <see cref="LlmEndpoints"/> provider (no dedicated
/// <c>ILlmProvider</c>: it is driven through the OpenAI dialect), but its endpoint and model
/// are written by <c>orkeon init</c>, shipped in <c>examples/appsettings/appsettings.json</c>
/// and offered by Orkeon Studio. They live here so those three cannot drift apart.
/// </summary>
public static class DockerModelRunnerDefaults
{
    /// <summary>Docker Model Runner llama.cpp OpenAI-compatible endpoint.</summary>
#pragma warning disable S1075 // URIs should not be hardcoded — this is the product's fixed local endpoint
    public const string BaseUrl = "http://localhost:12434/engines/llama.cpp/v1";
#pragma warning restore S1075

    /// <summary>Default model (parity with <c>examples/appsettings/appsettings.json</c>).</summary>
    public const string DefaultModel = "ai/granite-4.0-h-tiny";

    /// <summary>
    /// The endpoint authenticates nothing, but the OpenAI dialect wants a key: this
    /// placeholder is what the committed template and <c>orkeon init</c> both write.
    /// </summary>
    public const string ApiKeyPlaceholder = "not-needed";
}
