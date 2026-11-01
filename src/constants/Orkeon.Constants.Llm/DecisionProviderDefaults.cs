namespace Orkeon.Constants.Llm;

/// <summary>
/// Base URLs, default models and key variables of the decision providers
/// (<see cref="DecisionProviderKeys"/>).
/// </summary>
public static class DecisionProviderDefaults
{
#pragma warning disable S1075 // URIs should not be hardcoded - an official, stable public endpoint
    /// <summary>
    /// TypeSafe's API root: <c>POST /systemone</c> is appended to it.
    /// From docs.typesafe.ai (API reference and openapi.json), 2026-10-27.
    /// </summary>
    public const string TypeSafeBaseUrl = "https://api.typesafe.ai/v1";

    /// <summary>Liquid's decision API root.</summary>
    public const string LiquidBaseUrl = "https://api.liquid.ai/decisions/v1";

    /// <summary>Kev's local address.</summary>
    public const string KevBaseUrl = "http://127.0.0.1:8009/v1";
#pragma warning restore S1075

    /// <summary>Liquid's default model.</summary>
    public const string LiquidModel = "d1.1";

    /// <summary>The environment variable Liquid's key is read from.</summary>
    public const string LiquidApiKeyVariable = "LIQUID_API_KEY";

    /// <summary>Kev's default model.</summary>
    public const string KevModel = "kev-latest";

    /// <summary>The environment variable Kev's optional key is read from.</summary>
    public const string KevApiKeyVariable = "KEV_API_KEY";

    /// <summary>
    /// TypeSafe's moving alias for the newest Jev release (<c>jev-1.14.0</c> on 2026-10-27).
    /// </summary>
    public const string TypeSafeModel = "jev-latest";

    /// <summary>The environment variable TypeSafe's own SDKs read the API key from.</summary>
    public const string TypeSafeApiKeyVariable = "TYPESAFE_API_KEY";
}
