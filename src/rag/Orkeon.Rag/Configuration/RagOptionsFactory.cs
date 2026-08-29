using Orkeon.Constants.Configuration;
using Microsoft.Extensions.Configuration;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Configuration;

/// <summary>
/// Builds the effective <see cref="RagOptions"/> from a profile preset and the
/// <c>Orkeon:Rag</c> configuration section (plan §8.1): profile = preset,
/// configuration = override — any individual key bound over the preset wins.
/// </summary>
public static class RagOptionsFactory
{
    /// <summary>Configuration section bound over the profile preset.</summary>
    public const string SectionKey = ConfigurationKeys.Rag;

    /// <summary>
    /// Builds the options for <paramref name="profileName"/> (or, when
    /// <c>null</c>, the profile named by <c>Orkeon:Rag:Profile</c> — default
    /// <see cref="RagDefaults.DefaultProfile"/>): the profile preset first, then
    /// every <c>Orkeon:Rag</c> configuration key bound over it.
    /// </summary>
    /// <exception cref="ArgumentException">The profile name is unknown — the message lists the known profiles.</exception>
    public static RagOptions Build(IConfiguration configuration, string? profileName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionKey);
        var name = profileName
            ?? section[nameof(RagOptions.Profile)]
            ?? RagDefaults.DefaultProfile;

        var profile = RagProfilePresets.Parse(name); // unknown name fails loudly
        var options = RagProfilePresets.Create(profile);

        // Configuration overrides the preset key by key; the Profile property is
        // pinned back to the canonical resolved name (an explicit profileName
        // must not be shadowed by a different Orkeon:Rag:Profile value).
        section.Bind(options);
        options.Profile = RagProfilePresets.NameOf(profile);

        return options;
    }
}
