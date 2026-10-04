using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-55: <c>"Temperature": 1e400</c>, written by hand in <c>studio-model-profiles.json</c>, reads
/// as infinity — a number JSON has no text for. The store refuses such a file like one it cannot
/// convert: the empty set and a reason naming the setting and the field, never a setting whose
/// writing fails in silence and whose launch passes <c>Infinity</c> to the run.
/// </summary>
public sealed class ModelProfileStoreFiniteTemperatureTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"orkeon-profile-finite-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { /* best-effort */ }
    }

    private async Task<ModelProfileLoadResult> LoadAsync(string temperature)
    {
        await File.WriteAllTextAsync(_path, $$"""
            {
              "Profiles": [
                { "Name": "Local", "Provider": "ollama", "BaseUrl": "http://localhost:11434", "Model": "qwen3" },
                { "Name": "Kimi", "Provider": "kimi", "BaseUrl": "https://api.moonshot.ai/v1", "Model": "kimi-k3", "Temperature": {{temperature}} }
              ],
              "DefaultProfile": "Local"
            }
            """, TestContext.Current.CancellationToken);
        return await new ModelProfileFileStore(_path).LoadAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("1e400", "Infinity")]
    [InlineData("-1e400", "-Infinity")]
    public async Task A_temperature_that_reads_as_an_infinity_refuses_the_file_with_its_reason(string written, string read)
    {
        var loaded = await LoadAsync(written);

        Assert.True(loaded.Failed);
        Assert.Empty(loaded.Set.Profiles);
        Assert.Contains("setting 'Kimi'", loaded.Error, StringComparison.Ordinal);
        Assert.Contains("Temperature is not a finite number", loaded.Error, StringComparison.Ordinal);
        Assert.Contains(read, loaded.Error, StringComparison.Ordinal);
        Assert.StartsWith(_path, loaded.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1e-400", 0.0)]
    [InlineData("0.7", 0.7)]
    public async Task A_finite_temperature_loads(string written, double read)
    {
        var loaded = await LoadAsync(written);

        Assert.False(loaded.Failed);
        Assert.Equal(read, loaded.Set.Profiles.Single(profile => profile.Name == "Kimi").Temperature);
    }

    [Fact]
    public async Task A_null_in_the_list_is_no_setting_and_does_not_throw()
    {
        await File.WriteAllTextAsync(_path, """
            { "Profiles": [ null, { "Name": "Local", "Provider": "ollama", "BaseUrl": "http://localhost:11434", "Model": "qwen3", "Temperature": 0.2 } ] }
            """, TestContext.Current.CancellationToken);

        var loaded = await new ModelProfileFileStore(_path).LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(loaded.Failed);
        Assert.Equal("Local", Assert.Single(loaded.Set.Profiles).Name);
    }
}
