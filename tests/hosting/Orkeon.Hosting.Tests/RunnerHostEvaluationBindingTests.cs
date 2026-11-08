using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-09 - the runner host reads the <c>Evaluation</c> section (it used to call the overload of the
/// infrastructure that binds nothing) and registers the benchmark runner <c>orkeon eval bench</c> uses.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostEvaluationBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-eval-binding-" + Guid.NewGuid().ToString("N"));

    public RunnerHostEvaluationBindingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private Microsoft.Extensions.Hosting.IHost Build(string evaluationSection)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, "{ \"RaggableTree\": { \"Enabled\": false }, \"Llm\": { \"Model\": \"m\", \"ApiKey\": \"sk-test\" }" + evaluationSection + " }");
        return RunnerHost.Build(path, new RunnerMountPlan { CliMounts = [$"{_root}:/workspace:ro"], AllowExternalMounts = true });
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldReadEnableLlmJudge_WhenTheEvaluationSectionIsSet()
    {
        using var host = Build(", \"Evaluation\": { \"EnableLlmJudge\": true }");

        Assert.True(host.Services.GetRequiredService<IOptions<EvaluationOptions>>().Value.EnableLlmJudge);
        var names = host.Services.GetRequiredService<IEvaluationSuite>().Evaluators.Select(e => e.Name).ToList();
        Assert.Contains("Coherence", names);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldKeepTheJudgesOff_WhenTheSectionIsAbsent()
    {
        using var host = Build("");

        Assert.False(host.Services.GetRequiredService<IOptions<EvaluationOptions>>().Value.EnableLlmJudge);
        var names = host.Services.GetRequiredService<IEvaluationSuite>().Evaluators.Select(e => e.Name).ToList();
        Assert.DoesNotContain("Coherence", names);
        Assert.Contains("Similarity", names);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldRegisterTheBenchmarkRunner()
    {
        using var host = Build("");

        Assert.NotNull(host.Services.GetService<IBenchmarkRunner>());
    }
}
