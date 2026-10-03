using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ILlmProviderFactory"/>: one <see cref="StubLlmProvider"/> per
/// configuration it is asked to build — the default's and each profile's —, named after the
/// configuration's model and answering every call with <see cref="Answer"/>. A runner test reads
/// which provider a call reached, without any network.
/// </summary>
public sealed class RecordingLlmProviderFactory : ILlmProviderFactory
{
    private readonly List<StubLlmProvider> _built = [];
    private readonly Lock _lock = new();

    /// <summary>The fixed answer of every provider built.</summary>
    public string Answer { get; init; } = "Final answer.";

    /// <summary>Every provider built so far, in build order.</summary>
    public IReadOnlyList<StubLlmProvider> Built
    {
        get { lock (_lock) { return [.. _built]; } }
    }

    /// <summary>The provider built for the configuration whose model is <paramref name="model"/>, or null.</summary>
    public StubLlmProvider? For(string model)
    {
        lock (_lock)
            return _built.FirstOrDefault(provider => string.Equals(provider.Name, model, StringComparison.Ordinal));
    }

    public IBasicLlmProvider Create(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var answer = new LlmResponse { Content = Answer };
        var provider = new StubLlmProvider { Name = config.Model, BaseConfig = config }
            .RespondWith(answer)
            .RespondToChatWith(answer);
        lock (_lock)
            _built.Add(provider);
        return new LlmProviderAdapter(provider);
    }
}
