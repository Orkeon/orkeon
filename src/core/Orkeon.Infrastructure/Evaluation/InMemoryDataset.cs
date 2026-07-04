using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Dataset backed by an in-memory list of evaluation inputs (useful for testing).
/// </summary>
public sealed class InMemoryDataset : IEvaluationDataset
{
    private readonly IReadOnlyList<EvaluationInput> _inputs;

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Initializes a new instance of <see cref="InMemoryDataset"/>.</summary>
    /// <param name="name">The dataset name.</param>
    /// <param name="inputs">The evaluation inputs.</param>
    public InMemoryDataset(string name, IReadOnlyList<EvaluationInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ArgumentNullException.ThrowIfNull(inputs);
        _inputs = inputs;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EvaluationInput>> LoadAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_inputs);
    }
}
