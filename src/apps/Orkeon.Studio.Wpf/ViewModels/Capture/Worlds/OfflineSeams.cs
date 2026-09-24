using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// An endpoint probe that answers without a network.
/// <para>
/// Two things at once. It makes the connection-was-tested verdict photographable — the real probe would
/// need a live model behind a real URL — and it makes an outbound HTTP call from a headless
/// campaign structurally impossible rather than a rule somebody has to remember.
/// </para>
/// </summary>
internal sealed class OfflineLlmProbe : ILlmEndpointProbe
{
    /// <summary>What the next probe answers; a stop sets it before opening the editor.</summary>
    public LlmProbeResult Result { get; set; } = LlmProbeResult.Reachable(14);

    /// <inheritdoc />
    public Task<LlmProbeResult> ProbeAsync(LlmProbeRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result);
}

/// <summary>
/// A key store that forgets.
/// <para>
/// The real one writes <see cref="EnvironmentVariableTarget.User"/> — persistently, on the
/// operator's account. A campaign that walks every settings screen must not be able to do that, so
/// the capture world hands the editor a store whose keys live and die with the process.
/// </para>
/// </summary>
internal sealed class EphemeralApiKeyStore : IApiKeyStore
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? Peek(string envName) =>
        _keys.TryGetValue(envName, out var value) ? value : null;

    /// <inheritdoc />
    public void Save(string envName, string value) => _keys[envName] = value;
}

/// <summary>
/// The campaign's clock, stopped at the seeded world's own «now».
/// <para>
/// A run in flight shows on the status bar how long it has gone (STUDIO-34). Read off the
/// machine's clock, that figure would differ in every pass — and read against a stream stamped on
/// a fixed day, it would say how long ago that day was — so a diff of two campaigns would light
/// up on a stop that changed nothing.
/// </para>
/// </summary>
internal sealed class PinnedClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;
}
