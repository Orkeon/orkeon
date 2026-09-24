using System.Diagnostics.CodeAnalysis;
using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// The account a balance read asks about (STUDIO-35 D-01): the provider, the host its endpoint
/// names and the environment variable its key lives in — never the key itself. Two model
/// profiles that share all three share the account, so one request answers both. The host is
/// part of it because a provider can run two platforms whose keys do not cross: a key of Kimi's
/// .cn platform is refused by the .ai host.
/// </summary>
/// <param name="Provider">The provider <see cref="LlmProviderDetector"/> infers from the endpoint.</param>
/// <param name="Host">The endpoint's host, as <see cref="Uri"/> normalizes it; empty when the endpoint does not parse.</param>
/// <param name="KeyVariable">
/// The variable the key is read from: the profile's own, else <see cref="LlmPresets.DefaultApiKeyEnv"/>,
/// the one the runtime reads when a profile names none.
/// </param>
public sealed record ProviderBalanceAccount(string Provider, string Host, string KeyVariable)
{
    /// <summary>The account behind an endpoint and a key variable.</summary>
    [SuppressMessage("Design", "CA1054",
        Justification = "The input is the raw 'BaseUrl' field of a profile, which may be half-typed; " +
                        "an endpoint that does not parse is an account with no host, not an exception.")]
    public static ProviderBalanceAccount For(string? baseUrl, string? keyVariable) => new(
        LlmProviderDetector.Detect(baseUrl),
        Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var endpoint) ? endpoint.Host : string.Empty,
        string.IsNullOrWhiteSpace(keyVariable) ? LlmPresets.DefaultApiKeyEnv : keyVariable.Trim());

    /// <summary>The account a profile runs on.</summary>
    public static ProviderBalanceAccount For(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return For(profile.BaseUrl, profile.KeyEnvName);
    }

    /// <summary>
    /// Whether an endpoint has an account behind it at all. It has none when it is missing, or
    /// when it is a runtime on this machine or its Docker host — which is also what the probe
    /// answers for one («not applicable»), without a request.
    /// </summary>
    [SuppressMessage("Design", "CA1054",
        Justification = "The input is the raw 'BaseUrl' field of a profile, which may be half-typed.")]
    public static bool HasAccount(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var endpoint))
            return false;

        if (endpoint.IsLoopback || string.Equals(endpoint.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase))
            return false;

        return LlmProviderDetector.Detect(baseUrl) is not (LlmProviderKeys.Ollama or LlmProviderKeys.DockerModelRunner);
    }
}

/// <summary>
/// One account to read, the endpoint the read goes to and the profiles it answers for — what a
/// balance read covers, one request per account (STUDIO-35 D-01).
/// </summary>
/// <param name="Account">The account, the identity two profiles share.</param>
/// <param name="BaseUrl">The endpoint of the first profile on the account: the host the request goes to (STUDIO-33 D-04).</param>
/// <param name="Profiles">The names of the profiles on the account, in the order they came.</param>
[SuppressMessage("Design", "CA1054",
    Justification = "The endpoint is the profile's own text, handed to the probe as it takes it.")]
[SuppressMessage("Design", "CA1056",
    Justification = "The endpoint is the profile's own text, handed to the probe as it takes it.")]
public sealed record ProviderBalanceTarget(ProviderBalanceAccount Account, string? BaseUrl, IReadOnlyList<string> Profiles)
{
    /// <summary>
    /// The accounts <paramref name="profiles"/> run on, one target each: profiles sharing an
    /// account share its target, and a profile with no account behind it (see
    /// <see cref="ProviderBalanceAccount.HasAccount"/>) has none.
    /// </summary>
    public static IReadOnlyList<ProviderBalanceTarget> For(IEnumerable<ModelProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var targets = new List<ProviderBalanceTarget>();
        foreach (var profile in profiles.Where(p => ProviderBalanceAccount.HasAccount(p.BaseUrl)))
        {
            var account = ProviderBalanceAccount.For(profile);
            var index = targets.FindIndex(t => t.Account == account);
            if (index < 0)
                targets.Add(new ProviderBalanceTarget(account, profile.BaseUrl, [profile.Name]));
            else if (!targets[index].Profiles.Contains(profile.Name, StringComparer.Ordinal))
                targets[index] = targets[index] with { Profiles = [.. targets[index].Profiles, profile.Name] };
        }

        return targets;
    }

    /// <summary>
    /// The request that reads this account: its endpoint, and the key a run on it would present
    /// — <paramref name="typedKey"/> first (a key typed in the editor and not yet remembered),
    /// then the account's variable, then the runtime's own, which a run inherits when the
    /// profile's variable is empty.
    /// </summary>
    public LlmProbeRequest RequestWith(IApiKeyStore keys, string? typedKey = null)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var key = typedKey is { } typed && !string.IsNullOrWhiteSpace(typed)
            ? typed.Trim()
            : keys.Peek(Account.KeyVariable)
              ?? (string.Equals(Account.KeyVariable, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal)
                  ? null
                  : keys.Peek(LlmPresets.DefaultApiKeyEnv));

        return new LlmProbeRequest { BaseUrl = BaseUrl, ApiKey = key };
    }
}
