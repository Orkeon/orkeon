using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Routing;

namespace Orkeon.Rag.Tests.Routing;

/// <summary>
/// Tests of the deterministic rule-based classifier (RAG-05/C3): the case
/// table mirrors the documented rules (social openers → NoRetrieval; multi-hop
/// signals → Iterative; short imperative → NoRetrieval; default → SingleShot),
/// plus determinism across calls and instances.
/// </summary>
public class HeuristicQueryComplexityClassifierTests
{
    [Theory]
    // Rule 1 — empty query.
    [InlineData("", QueryRoute.NoRetrieval)]
    [InlineData("   \t  ", QueryRoute.NoRetrieval)]
    // Rule 2 — greetings / social / opinion openers.
    [InlineData("Hello! How is it going?", QueryRoute.NoRetrieval)]
    [InlineData("hi there", QueryRoute.NoRetrieval)]
    [InlineData("Thanks a lot for your help", QueryRoute.NoRetrieval)]
    [InlineData("Bonjour, comment ça va ?", QueryRoute.NoRetrieval)]
    [InlineData("merci beaucoup", QueryRoute.NoRetrieval)]
    // Rule 3 — two or more interrogative words.
    [InlineData("Who founded the company and when did it go public?", QueryRoute.Iterative)]
    [InlineData("What changed between v1 and v2 and why was the API redesigned?", QueryRoute.Iterative)]
    // Rule 3 — comparative markers.
    [InlineData("Compare the pricing of plan A with plan B", QueryRoute.Iterative)]
    [InlineData("PostgreSQL versus MySQL for OLTP workloads", QueryRoute.Iterative)]
    [InlineData("What is the difference between TCP and UDP?", QueryRoute.Iterative)]
    // Rule 3 — multiple question marks.
    [InlineData("Is it fast? Is it safe? Should we adopt it?", QueryRoute.Iterative)]
    // Rule 3 — long query (>= 25 words).
    [InlineData("Summarize the architectural decisions taken across the last three quarterly reports including the migration plan the budget adjustments the staffing changes and the vendor negotiations that affected the platform team", QueryRoute.Iterative)]
    // Rule 4 — short imperative, no question mark, no interrogative word.
    [InlineData("summarize this", QueryRoute.NoRetrieval)]
    [InlineData("Translate that text", QueryRoute.NoRetrieval)]
    // Rule 5 — default: single factual question.
    [InlineData("What is the capital of Australia?", QueryRoute.SingleShot)]
    [InlineData("When was the SLA clause last updated?", QueryRoute.SingleShot)]
    [InlineData("List the retention periods defined in the data policy", QueryRoute.SingleShot)]
    public async Task Classify_RuleTable_ReturnsExpectedRoute(string query, QueryRoute expected)
    {
        var classifier = new HeuristicQueryComplexityClassifier();

        var route = await classifier.ClassifyAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(expected, route);
    }

    [Fact]
    public async Task Classify_IsDeterministic_AcrossCallsAndInstances()
    {
        string[] queries =
        [
            "Hello there!",
            "What is the capital of Australia?",
            "Compare plan A with plan B",
            "summarize this",
        ];

        foreach (var query in queries)
        {
            var first = await new HeuristicQueryComplexityClassifier()
                .ClassifyAsync(query, TestContext.Current.CancellationToken);

            for (var i = 0; i < 3; i++)
            {
                var again = await new HeuristicQueryComplexityClassifier()
                    .ClassifyAsync(query, TestContext.Current.CancellationToken);
                Assert.Equal(first, again);
            }
        }
    }

    [Fact]
    public async Task Classify_NullQuery_Throws()
    {
        var classifier = new HeuristicQueryComplexityClassifier();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => classifier.ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Classify_CancelledToken_Throws()
    {
        var classifier = new HeuristicQueryComplexityClassifier();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => classifier.ClassifyAsync("What is the capital of Australia?", cts.Token));
    }
}
