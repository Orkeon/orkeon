using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// GAP-15: <c>include_statements</c>, <c>summarizer_model</c>, <c>summarizer_max_tokens</c> and
/// <c>summarizer_concurrency</c> left <see cref="IndexCodebaseRequest"/> (nothing read them). An
/// agent that still sends one must not break its <c>index_codebase</c> call: the production
/// serializer of the typed tool pipeline ignores the field and reads the rest.
/// </summary>
public class IndexCodebaseRequestToleranceTests
{
    [Fact]
    public void ShouldIgnoreRetiredSettings_WhenAnAgentStillSendsThem()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["root_path"] = "/src",
            ["exclude"] = new[] { "vendor" },
            ["include_statements"] = true,
            ["summarizer_model"] = "claude-haiku-4-5",
            ["summarizer_max_tokens"] = 120,
            ["summarizer_concurrency"] = 5,
        };

        var request = JsonComponentSerializer.Instance.Deserialize<IndexCodebaseRequest>(parameters);

        Assert.Equal("/src", request.RootPath);
        Assert.Equal(["vendor"], request.Exclude);
    }
}
