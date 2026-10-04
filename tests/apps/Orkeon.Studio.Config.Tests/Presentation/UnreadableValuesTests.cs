using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Config.Views;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Config.Tests.Presentation;

/// <summary>
/// STUDIO-55: the terminal editor never keeps nor erases in silence a value it cannot read. A
/// temperature that is no finite number is a field error; a field shows what the file holds, as
/// written, and an unreadable value is refused until it is changed — a tri-state switch included,
/// which keeps the text the file holds until the switch changes state.
/// </summary>
public sealed class UnreadableValuesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("studio-config-unreadable").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private async Task<ConfigEditorModel> OpenAsync(string json)
    {
        var path = Path.Combine(_directory, "appsettings.json");
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        var model = new ConfigEditorModel(
            new FakeDirectoryProbe("/data/in"),
            new OrkeonProcessRunner(new FakeProcessLauncher(), new OrkeonBinaryLocator(new FakeExecutableProbe(), ["orkeon"])),
            new FakeLlmEndpointProbe());
        Assert.Null(await model.OpenAsync(path, TestContext.Current.CancellationToken));
        return model;
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public async Task A_temperature_that_is_no_finite_number_is_a_field_error_and_the_json_view_still_renders(string typed)
    {
        var model = await OpenAsync("""{ "Llm": { "Model": "qwen3", "Temperature": 0.2 } }""");

        model.Llm.Temperature = typed;
        var errors = model.ApplyForms();

        Assert.Equal($"Llm:Temperature: '{typed}' is not a number.", Assert.Single(errors));
        Assert.Equal(0.2, model.Document.Llm.Temperature);
        Assert.Contains("\"Temperature\": 0.2", model.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_shows_what_the_file_holds_and_an_unreadable_value_is_refused_and_kept()
    {
        var model = await OpenAsync("""
            {
              "Llm": { "Model": "qwen3", "TimeoutSeconds": 600.0, "MaxTokens": "4096x", "Thinking": { "Enabled": "yes" } },
              "RateLimiting": { "QueueLimit": 32.5 },
              "LlmLogging": { "MaxBodyLengthChars": "lots" },
              "Orkeon": { "Rag": { "Corrective": { "MaxIterations": 2.5 } } }
            }
            """);

        Assert.Equal("600.0", model.Llm.TimeoutSeconds);
        Assert.Equal("4096x", model.Llm.MaxTokens);
        Assert.Equal("yes", model.Llm.ThinkingEnabled);
        Assert.Equal("32.5", model.RateLimiting.QueueLimit);
        Assert.Equal("lots", model.LlmLogging.MaxBodyLengthChars);
        Assert.Equal("2.5", model.Rag.CorrectiveMaxIterations);

        var errors = model.ApplyForms();

        Assert.Contains("Llm:TimeoutSeconds: '600.0' is not a whole number.", errors);
        Assert.Contains("Llm:MaxTokens: '4096x' is not a whole number.", errors);
        Assert.Contains("Llm:Thinking:Enabled: 'yes' is not true or false.", errors);
        Assert.Contains("RateLimiting:QueueLimit: '32.5' is not a whole number.", errors);
        Assert.Contains("LlmLogging:MaxBodyLengthChars: 'lots' is not a whole number.", errors);
        Assert.Contains("Orkeon:Rag:Corrective:MaxIterations: '2.5' is not a whole number.", errors);
        Assert.Equal("600.0", model.Document.GetString("Llm:TimeoutSeconds"));
        Assert.Equal("4096x", model.Document.GetString("Llm:MaxTokens"));
        Assert.Equal("yes", model.Document.GetString("Llm:Thinking:Enabled"));
        Assert.Equal("32.5", model.Document.GetString("RateLimiting:QueueLimit"));
    }

    [Fact]
    public async Task A_switch_that_holds_no_boolean_keeps_its_text_and_is_refused()
    {
        var model = await OpenAsync("""
            {
              "Llm": { "Model": "qwen3" },
              "LlmLogging": { "FullEmbeddingLog": "yes", "LogStreamingExchanges": true },
              "Orkeon": { "Rag": { "Retrieval": { "Hybrid": { "Enabled": 1 } } } }
            }
            """);

        Assert.Null(model.LlmLogging.FullEmbeddingLog);
        Assert.Equal("yes", model.LlmLogging.FullEmbeddingLogAsWritten);
        Assert.Null(model.LlmLogging.LogStreamingExchangesAsWritten);
        Assert.Equal("1", model.Rag.HybridRetrievalEnabledAsWritten);

        var errors = model.ApplyForms();

        Assert.Contains("LlmLogging:FullEmbeddingLog: 'yes' is not true or false.", errors);
        Assert.Contains("Orkeon:Rag:Retrieval:Hybrid:Enabled: '1' is not true or false.", errors);
        Assert.Equal("yes", model.Document.GetString("LlmLogging:FullEmbeddingLog"));
        Assert.Equal("1", model.Document.GetString("Orkeon:Rag:Retrieval:Hybrid:Enabled"));
    }

    [Fact]
    public async Task A_switch_changed_by_the_user_replaces_the_unreadable_value()
    {
        var model = await OpenAsync("""
            { "Llm": { "Model": "qwen3" }, "LlmLogging": { "FullEmbeddingLog": "yes", "LogStreamingExchanges": "on" } }
            """);

        // Touched, then left « unset »: the key goes, without an error.
        model.LlmLogging.FullEmbeddingLogAsWritten = null;
        model.LlmLogging.FullEmbeddingLog = null;
        // Set to « yes »: true is written.
        model.LlmLogging.LogStreamingExchangesAsWritten = null;
        model.LlmLogging.LogStreamingExchanges = true;

        Assert.Empty(model.ApplyForms());
        Assert.False(model.Document.ContainsPath("LlmLogging:FullEmbeddingLog"));
        Assert.True(model.Document.GetBoolean("LlmLogging:LogStreamingExchanges"));
    }

    [Fact]
    public void The_switch_caption_says_the_text_the_file_holds()
    {
        Assert.Equal("Log streaming exchanges", FormLayout.SwitchCaption("Log streaming exchanges", null));
        Assert.Equal(
            "Log streaming exchanges — the file holds 'on', not true or false",
            FormLayout.SwitchCaption("Log streaming exchanges", "on"));
    }
}
