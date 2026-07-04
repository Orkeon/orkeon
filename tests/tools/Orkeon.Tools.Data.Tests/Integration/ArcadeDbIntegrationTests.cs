using Orkeon.Tools.Data.Graph;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for ArcadeDB.
/// ArcadeDB has no official Testcontainers module; these tests require a running
/// ArcadeDB instance and are reported as skipped (not passed) when none is available.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ArcadeDbIntegrationTests : DatabaseTestFixture, IAsyncLifetime
{
    private const string SkipReason =
        "ArcadeDB server not available at localhost:2480 (no official Testcontainers module)";

    private bool _containerAvailable;

    public async ValueTask InitializeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync("http://localhost:2480/api/v1/server", cts.Token);
            _containerAvailable = response.IsSuccessStatusCode;
        }
        catch
        {
            _containerAvailable = false;
        }

        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    [Trait("Category", "Integration")]
    public void ToolCreation_Succeeds()
    {
        Assert.SkipWhen(!_containerAvailable, SkipReason);

        using var tool = new ArcadeDbTool();
        Assert.NotNull(tool);
    }
}
