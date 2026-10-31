using Microsoft.Extensions.Configuration;

namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>How the active decision provider was chosen.</summary>
public enum DecisionProviderSelection
{
    /// <summary>Named by <c>Orkeon:Decision:Provider</c>.</summary>
    Named,

    /// <summary>Deduced from a section or a key.</summary>
    Deduced,
}

/// <summary>The active decision provider and how it was chosen.</summary>
public sealed record DecisionProviderChoice
{
    private const string ProviderSetting = "Orkeon:Decision:Provider";

    /// <summary>Gets the provider, or null when none is chosen.</summary>
    public DecisionProviderDescriptor? Provider { get; init; }

    /// <summary>Gets how the provider was chosen.</summary>
    public DecisionProviderSelection How { get; init; }

    /// <summary>Gets the present provider sections that are not used.</summary>
    public IReadOnlyList<string> IgnoredSections { get; init; } = [];

    /// <summary>Gets the choice of no provider.</summary>
    public static DecisionProviderChoice None { get; } = new();

    /// <summary>Gets a value indicating whether a provider is chosen.</summary>
    public bool HasProvider => Provider is not null;

    /// <summary>Resolves the active provider.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment reader.</param>
    /// <returns>The choice.</returns>
    public static DecisionProviderChoice Resolve(IConfiguration configuration, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var hosted = DecisionProviderDescriptor.All.Where(d => d.Hosted).ToList();
        var name = configuration[ProviderSetting];
        DecisionProviderDescriptor? provider;
        DecisionProviderSelection how;
        if (!string.IsNullOrWhiteSpace(name))
        {
            provider = DecisionProviderDescriptor.Find(name.Trim())
                ?? throw new InvalidOperationException(
                    $"{ProviderSetting} is '{name}'; valid names are {string.Join(", ", DecisionProviderDescriptor.All.Select(d => d.Key))}.");
            how = DecisionProviderSelection.Named;
        }
        else
        {
            provider = Deduce(hosted.Where(d => HasSection(configuration, d.Section)).ToList())
                ?? Deduce(hosted.Where(d => !string.IsNullOrEmpty(environment(d.DefaultApiKeyVariable))).ToList());
            how = DecisionProviderSelection.Deduced;
        }

        var ignored = DecisionProviderDescriptor.All
            .Where(d => d != provider && HasSection(configuration, d.Section))
            .Select(d => d.Section)
            .ToList();

        if (provider is null)
            return new DecisionProviderChoice { IgnoredSections = ignored };

        if (!provider.Hosted)
            CheckKevAddress(configuration, environment, provider);

        return new DecisionProviderChoice { Provider = provider, How = how, IgnoredSections = ignored };
    }

    /// <summary>Tells whether a section holds a key, even an empty one.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="section">The section path.</param>
    /// <returns>True when present.</returns>
    public static bool HasSection(IConfiguration configuration, string section)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(section);
        var parentPath = ConfigurationPath.GetParentPath(section);
        var parent = parentPath is null ? configuration.GetChildren() : configuration.GetSection(parentPath).GetChildren();
        var leaf = ConfigurationPath.GetSectionKey(section);
        return parent.Any(c => string.Equals(c.Key, leaf, StringComparison.OrdinalIgnoreCase));
    }

    private static DecisionProviderDescriptor? Deduce(List<DecisionProviderDescriptor> found)
    {
        if (found.Count > 1)
            throw new InvalidOperationException(
                $"Both {found[0].DisplayName} and {found[1].DisplayName} are configured for the decision model; name one with {ProviderSetting}.");
        return found.Count == 1 ? found[0] : null;
    }

    private static void CheckKevAddress(
        IConfiguration configuration, Func<string, string?> environment, DecisionProviderDescriptor kev)
    {
        var raw = configuration[kev.Section + ":BaseUrl"];
        var address = string.IsNullOrWhiteSpace(raw) ? kev.DefaultBaseUrl : ParseAddress(raw, kev);

        var variable = configuration[kev.Section + ":ApiKeyEnvVar"];
        if (string.IsNullOrWhiteSpace(variable))
            variable = kev.DefaultApiKeyVariable;
        var keyConfigured = !string.IsNullOrEmpty(configuration[kev.Section + ":ApiKey"])
            || !string.IsNullOrEmpty(environment(variable));

        if (keyConfigured && address.Scheme != Uri.UriSchemeHttps && !address.IsLoopback)
            throw new InvalidOperationException(
                $"{kev.Section}:BaseUrl '{address}' is neither https nor a loopback address: the API key would travel in clear to another machine.");
    }

    private static Uri ParseAddress(string raw, DecisionProviderDescriptor kev)
        => Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : throw new InvalidOperationException(
                $"{kev.Section}:BaseUrl '{raw}' is not an absolute http or https address.");
}
