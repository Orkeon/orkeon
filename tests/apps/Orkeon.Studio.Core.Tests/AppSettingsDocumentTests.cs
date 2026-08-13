using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// The load/edit/save contract: everything Studio does not model must come back out
/// of the document exactly as it went in.
/// </summary>
public sealed class AppSettingsDocumentTests
{
    private static readonly string[] RootKeysInOrder =
        ["Llm", "TotallyUnknownSection", "RateLimiting", "_comment"];

    private static readonly string[] LlmKeysInOrder =
        ["Model", "BaseUrl", "Temperature", "SomeFutureKnob"];

    private static readonly string[] DeeperEntries = ["a", "b"];

    private const string DocumentWithUnknownKeys = """
        {
          "Llm": {
            "Model": "gpt-5.6-sol",
            "BaseUrl": "https://api.openai.com/v1",
            "Temperature": 0.7,
            "SomeFutureKnob": { "nested": [1, 2, 3] }
          },
          "TotallyUnknownSection": {
            "Deep": { "Deeper": ["a", "b"], "Flag": true },
            "Number": 42
          },
          "RateLimiting": { "MaxConcurrentRequests": 1 },
          "_comment": "kept as-is"
        }
        """;

    [Fact]
    public void Round_trip_preserves_unknown_keys_values_and_order()
    {
        var document = AppSettingsDocument.Parse(DocumentWithUnknownKeys);

        var reparsed = AppSettingsDocument.Parse(document.ToJson());

        Assert.Equal(
            RootKeysInOrder,
            reparsed.Root.Select(pair => pair.Key));
        Assert.Equal("kept as-is", reparsed.GetString("_comment"));
        Assert.Equal(42, reparsed.GetInt32("TotallyUnknownSection:Number"));
        Assert.True(reparsed.GetBoolean("TotallyUnknownSection:Deep:Flag"));
        Assert.Equal(DeeperEntries, reparsed.GetStringArray("TotallyUnknownSection:Deep:Deeper"));
        Assert.Equal("[1,2,3]", reparsed.GetNode("Llm:SomeFutureKnob:nested")!.ToJsonString());
    }

    [Fact]
    public void Editing_a_known_field_leaves_every_other_key_untouched()
    {
        var document = AppSettingsDocument.Parse(DocumentWithUnknownKeys);

        document.Llm.Model = "llama3.2";

        var reparsed = AppSettingsDocument.Parse(document.ToJson());
        Assert.Equal("llama3.2", reparsed.Llm.Model);
        Assert.Equal(0.7, reparsed.Llm.Temperature);
        Assert.Equal(
            LlmKeysInOrder,
            ((JsonObject)reparsed.GetNode("Llm")!).Select(pair => pair.Key));
        Assert.NotNull(reparsed.GetNode("TotallyUnknownSection:Deep:Deeper"));
    }

    [Fact]
    public void Round_trip_of_the_shipped_sample_is_stable()
    {
        // examples/appsettings/appsettings.json, the file the archives ship as
        // appsettings.sample.json.
        const string sample = """
            {
              "Llm": {
                "Model": "ai/granite-4.0-h-tiny",
                "BaseUrl": "http://localhost:12434/engines/llama.cpp/v1",
                "ApiKey": "not-needed",
                "Temperature": 0.7,
                "MaxTokens": 4096,
                "TimeoutSeconds": 120
              },
              "RateLimiting": {
                "MaxConcurrentRequests": 1,
                "GlobalRequestsPerMinute": 60,
                "ProviderRequestsPerMinute": 30,
                "AgentRequestsPerMinute": 20,
                "QueueLimit": 32
              }
            }
            """;

        var once = AppSettingsDocument.Parse(sample).ToJson();
        var twice = AppSettingsDocument.Parse(once).ToJson();

        Assert.Equal(once, twice);
        Assert.EndsWith(Environment.NewLine, once, StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_a_null_value_removes_the_key_instead_of_writing_json_null()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m", "ApiKey": "secret" } }""");

        document.Llm.ApiKey = null;

        Assert.False(document.ContainsPath("Llm:ApiKey"));
        Assert.DoesNotContain("null", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_a_nested_path_creates_the_intermediate_objects()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.SetBoolean("Orkeon:Rag:Retrieval:Hybrid:Enabled", true);

        Assert.True(document.GetBoolean("Orkeon:Rag:Retrieval:Hybrid:Enabled"));
        Assert.True(document.SectionExists("Orkeon:Rag"));
    }

    [Fact]
    public void Removing_an_absent_nested_path_does_not_materialize_parents()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Remove("Orkeon:Rag:Profile");

        Assert.Equal("{}" + Environment.NewLine, document.ToJson());
    }

    [Fact]
    public void Numbers_and_booleans_are_read_from_their_string_spelling()
    {
        // The configuration binder accepts "4096" for an int; so does the editor.
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "MaxTokens": "4096" }, "LlmLogging": { "FullEmbeddingLog": "false" } }""");

        Assert.Equal(4096, document.Llm.MaxTokens);
        Assert.False(document.LlmLogging.FullEmbeddingLog);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_accepted_on_load()
    {
        const string jsonc = """
            {
              // the runtime's JSON configuration provider accepts this too
              "Llm": { "Model": "m", },
            }
            """;

        Assert.True(AppSettingsDocument.TryParse(jsonc, out var document, out var error));
        Assert.Null(error);
        Assert.Equal("m", document.Llm.Model);
    }

    [Fact]
    public void TryParse_reports_malformed_json_instead_of_throwing()
    {
        Assert.False(AppSettingsDocument.TryParse("{ not json", out var document, out var error));
        Assert.Null(document);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_rejects_a_non_object_root()
    {
        Assert.False(AppSettingsDocument.TryParse("[1, 2]", out _, out var error));
        Assert.Contains("object", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clone_is_independent_of_the_source_document()
    {
        var document = AppSettingsDocument.Parse(DocumentWithUnknownKeys);
        var clone = document.Clone();

        clone.Llm.Model = "changed";

        Assert.Equal("gpt-5.6-sol", document.Llm.Model);
        Assert.Equal("changed", clone.Llm.Model);
    }

    [Fact]
    public void An_empty_section_does_not_count_as_present()
    {
        // Mirrors IConfigurationSection.Exists(): a section with no keys is absent.
        var document = AppSettingsDocument.Parse("""{ "Llm": {} }""");

        Assert.False(document.Llm.Exists);
    }
}
