using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Training;

namespace Orkeon.Infrastructure.Tests.Training;

public class AutomaticFeedbackCollectorTestsFixture
{
    public static AutomaticFeedbackCollector CreateCollector()
    {
        var evaluators = Array.Empty<IEvaluator>();
        var logger = NullLogger<AutomaticFeedbackCollector>.Instance;
        return new AutomaticFeedbackCollector(evaluators, logger);
    }
}
