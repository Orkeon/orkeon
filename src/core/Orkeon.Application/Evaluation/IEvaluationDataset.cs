namespace Orkeon.Application.Evaluation;

/// <summary>
/// A loadable set of evaluation inputs.
/// </summary>
public interface IEvaluationDataset
{
    /// <summary>
    /// Name of this dataset.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Loads the evaluation inputs from the underlying source.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<EvaluationInput>> LoadAsync(CancellationToken ct = default);
}
