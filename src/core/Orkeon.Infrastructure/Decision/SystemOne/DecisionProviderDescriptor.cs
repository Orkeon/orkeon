using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>Describes one System One provider: where it is, which model and key it takes.</summary>
/// <param name="Key">The provider key.</param>
/// <param name="DisplayName">The name failures cite.</param>
/// <param name="Section">The configuration section.</param>
/// <param name="DefaultBaseUrl">The API root.</param>
/// <param name="DefaultModel">The model a request naming none runs on.</param>
/// <param name="DefaultApiKeyVariable">The key environment variable.</param>
/// <param name="KeyRequired">Whether a request without a key is refused.</param>
/// <param name="Hosted">Whether the provider is a hosted service.</param>
public sealed record DecisionProviderDescriptor(
    string Key,
    string DisplayName,
    string Section,
    Uri DefaultBaseUrl,
    string DefaultModel,
    string DefaultApiKeyVariable,
    bool KeyRequired,
    bool Hosted)
{
    /// <summary>Gets TypeSafe.</summary>
    public static DecisionProviderDescriptor TypeSafe { get; } = new(
        DecisionProviderKeys.TypeSafe,
        "TypeSafe",
        "Orkeon:TypeSafe",
        new Uri(DecisionProviderDefaults.TypeSafeBaseUrl),
        DecisionProviderDefaults.TypeSafeModel,
        DecisionProviderDefaults.TypeSafeApiKeyVariable,
        KeyRequired: true,
        Hosted: true);

    /// <summary>Gets Liquid.</summary>
    public static DecisionProviderDescriptor Liquid { get; } = new(
        DecisionProviderKeys.Liquid,
        "Liquid",
        "Orkeon:Liquid",
        new Uri(DecisionProviderDefaults.LiquidBaseUrl),
        DecisionProviderDefaults.LiquidModel,
        DecisionProviderDefaults.LiquidApiKeyVariable,
        KeyRequired: true,
        Hosted: true);

    /// <summary>Gets Kev.</summary>
    public static DecisionProviderDescriptor Kev { get; } = new(
        DecisionProviderKeys.Kev,
        "Kev",
        "Orkeon:Kev",
        new Uri(DecisionProviderDefaults.KevBaseUrl),
        DecisionProviderDefaults.KevModel,
        DecisionProviderDefaults.KevApiKeyVariable,
        KeyRequired: false,
        Hosted: false);

    /// <summary>Gets every descriptor.</summary>
    public static IReadOnlyList<DecisionProviderDescriptor> All { get; } = [TypeSafe, Liquid, Kev];

    /// <summary>Finds a descriptor by key, ignoring case.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The descriptor, or null.</returns>
    public static DecisionProviderDescriptor? Find(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sets the base URL, model and key variable of <paramref name="options"/>.</summary>
    /// <param name="options">The options.</param>
    public void ApplyDefaults(SystemOneProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.BaseUrl = DefaultBaseUrl;
        options.Model = DefaultModel;
        options.ApiKeyEnvVar = DefaultApiKeyVariable;
    }

    /// <summary>Gives the provider's default to every value the section bound blank.</summary>
    /// <param name="options">The bound options.</param>
    internal void FillBlanks(SystemOneProviderOptions options)
    {
        if (options.BaseUrl is null || string.IsNullOrWhiteSpace(options.BaseUrl.OriginalString))
            options.BaseUrl = DefaultBaseUrl;
        if (string.IsNullOrWhiteSpace(options.Model))
            options.Model = DefaultModel;
        if (string.IsNullOrWhiteSpace(options.ApiKeyEnvVar))
            options.ApiKeyEnvVar = DefaultApiKeyVariable;
    }
}
