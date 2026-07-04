using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class InMemoryDatasetTestsFixture
{
    public static InMemoryDataset CreateDataset(string name, IReadOnlyList<EvaluationInput> inputs)
        => new(name, inputs);

    public static Task<IReadOnlyList<EvaluationInput>> LoadAsync(InMemoryDataset dataset)
        => dataset.LoadAsync();
}
