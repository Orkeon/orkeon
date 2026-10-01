using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// One named profile a host offers: how to build its provider, and the configuration its chat
/// client merges per-call options into. Registered by
/// <c>AddOrkeonLlmProfiles(configuration)</c> for every <c>Llm:Profiles:&lt;name&gt;</c>, or by
/// <c>AddOrkeonLlmProfile(name, …)</c> for a provider the factory does not build.
/// </summary>
/// <param name="Name">The name a crew references.</param>
/// <param name="Provider">Builds the provider, once, from the container.</param>
/// <param name="BaseConfig">
/// The configuration the profile's chat client starts each call from — its model, key and
/// endpoint; null takes the provider's own.
/// </param>
public sealed record LlmProfileRegistration(
    string Name,
    Func<IServiceProvider, ILlmProvider> Provider,
    LlmConfig? BaseConfig = null);

/// <summary>
/// Which of its profiles a host lets crews name. <c>orkeon-host</c> runs crews it does not
/// control, and binds its <c>Orkeon:Host:LlmProfiles</c> allow-list here (GAP-17).
/// </summary>
public sealed class LlmProfileAccessOptions
{
    /// <summary>
    /// The profiles crews may name; null (the default) allows every profile the host defines.
    /// The default profile is always allowed.
    /// </summary>
    public IReadOnlyList<string>? AllowedProfiles { get; set; }
}

/// <summary>
/// The default <see cref="ILlmProfileRegistry"/>: the host's default provider (the container's
/// <see cref="ILlmProvider"/>, <see cref="IBasicLlmProvider"/> and <see cref="IChatClient"/>) plus
/// every <see cref="LlmProfileRegistration"/>, each built on first use, once, and held for the
/// container's lifetime. <c>AddOrkeonLlmProfile</c> meters a registration's provider for the
/// host's <see cref="ILlmUsageSink"/>.
/// </summary>
public sealed class LlmProfileRegistry : ILlmProfileRegistry
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, LlmProfileRegistration> _offered;
    private readonly ConcurrentDictionary<string, Lazy<LlmProfile>> _built = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<LlmProfile> _default;

    /// <summary>Builds the registry over the container's profile registrations.</summary>
    /// <param name="services">The container the providers are built from.</param>
    /// <param name="registrations">Every profile the host registered.</param>
    /// <param name="access">The host's allow-list; unset allows every registered profile.</param>
    /// <exception cref="InvalidOperationException">Two registrations share a name, or one uses the reserved default name.</exception>
    public LlmProfileRegistry(
        IServiceProvider services,
        IEnumerable<LlmProfileRegistration> registrations,
        IOptions<LlmProfileAccessOptions>? access = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);
        _services = services;

        var all = new Dictionary<string, LlmProfileRegistration>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var registration in registrations)
        {
            if (LlmProfiles.IsDefault(registration.Name))
                throw new InvalidOperationException(
                    $"'{LlmProfiles.Default}' is the reserved name of the host's default profile; a profile cannot take it.");
            if (!all.TryAdd(registration.Name, registration))
                throw new InvalidOperationException($"Two LLM profiles are named '{registration.Name}'.");
            order.Add(registration.Name);
        }

        var allowed = access?.Value.AllowedProfiles;
        var allowedSet = allowed is null ? null : new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        Names = [.. order.Where(name => allowedSet is null || allowedSet.Contains(name))];
        _offered = Names.ToDictionary(name => name, name => all[name], StringComparer.OrdinalIgnoreCase);

        _default = new Lazy<LlmProfile>(() => new LlmProfile
        {
            Name = LlmProfiles.Default,
            Provider = _services.GetRequiredService<ILlmProvider>(),
            BasicProvider = _services.GetRequiredService<IBasicLlmProvider>(),
            ChatClient = _services.GetRequiredService<IChatClient>(),
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; }

    /// <inheritdoc />
    public bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return LlmProfiles.IsDefault(name) || _offered.ContainsKey(name.Trim());
    }

    /// <inheritdoc />
    public LlmProfile Resolve(string? name)
    {
        if (name is null || LlmProfiles.IsDefault(name))
            return _default.Value;

        var key = name.Trim();
        if (!_offered.TryGetValue(key, out var registration))
            throw new InvalidOperationException(LlmProfiles.UnknownMessage(key, "A crew", Names));

        return _built.GetOrAdd(registration.Name, _ => new Lazy<LlmProfile>(() => Build(registration))).Value;
    }

    /// <summary>
    /// Builds a profile's three surfaces over one provider — the shape
    /// <c>AddOrkeonLlmProvider</c> gives the default — with the vendor's native tool-call
    /// parser when the provider is Anthropic, as the infrastructure default chat client does.
    /// </summary>
    private LlmProfile Build(LlmProfileRegistration registration)
    {
        // Metered by its registration (AddOrkeonLlmProfile), one of the two entrances of the
        // metered path — a second meter here would count every call twice.
        var provider = registration.Provider(_services);
        var nativeParser = MeteredLlmProvider.Unwrap(provider) is AnthropicLlmProvider
            ? new AnthropicToolCallParser()
            : null;

        return new LlmProfile
        {
            Name = registration.Name,
            Provider = provider,
            BasicProvider = new LlmProviderAdapter(provider),
            ChatClient = new LlmProviderToChatClientAdapter(
                provider,
                registration.BaseConfig ?? provider.BaseConfig,
                textFallbackParser: _services.GetService<IToolCallParser>(),
                nativeToolCallParser: nativeParser),
        };
    }
}
