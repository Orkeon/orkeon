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

    private static readonly string[] NumericPaths = ["Llm:MaxTokens", "Llm:TimeoutSeconds", "Llm:StreamIdleSeconds", "Llm:Temperature"];

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
    public void The_llm_section_writes_and_clears_the_thinking_keys()
    {
        // LLM-11: the same Llm:Thinking:{Enabled,Effort} the runner reads.
        var document = AppSettingsDocument.CreateEmpty();

        document.Llm.ThinkingEnabled = false;
        document.Llm.ThinkingEffort = "high";
        Assert.False(document.Llm.ThinkingEnabled);
        Assert.Equal("high", document.Llm.ThinkingEffort);
        Assert.True(document.ContainsPath("Llm:Thinking:Enabled"));

        document.Llm.ThinkingEnabled = null;
        document.Llm.ThinkingEffort = null;
        Assert.Null(document.Llm.ThinkingEnabled);
        Assert.False(document.ContainsPath("Llm:Thinking:Enabled"));
        Assert.False(document.ContainsPath("Llm:Thinking:Effort"));
    }

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

    /// <summary>
    /// The sample an installation carries, <c>appsettings.sample.json</c>, is JSON with comments and
    /// a comma after every member — produced from the engine's settings, by category. An operator
    /// who copies it and opens it in Studio gets a document, and the model-call limits at the
    /// engine's defaults; the <c>Llm</c> section, which the file only shows, is not there.
    /// </summary>
    [Fact]
    public void The_sample_an_installation_ships_is_read_comments_and_all()
    {
        var document = AppSettingsDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "scripts", "installer-assets", "appsettings.sample.json")));

        Assert.Equal(RateLimitingSection.DefaultGlobalRequestsPerMinute, document.GetInt32("RateLimiting:GlobalRequestsPerMinute"));
        Assert.Equal(RateLimitingSection.DefaultProviderRequestsPerMinute, document.GetInt32("RateLimiting:ProviderRequestsPerMinute"));
        Assert.Equal(RateLimitingSection.DefaultAgentRequestsPerMinute, document.GetInt32("RateLimiting:AgentRequestsPerMinute"));
        Assert.Equal(RateLimitingSection.DefaultMaxConcurrentRequests, document.GetInt32("RateLimiting:MaxConcurrentRequests"));
        Assert.Equal(RateLimitingSection.DefaultQueueLimit, document.GetInt32("RateLimiting:QueueLimit"));
        Assert.False(document.ContainsPath("Llm"));
        Assert.NotNull(AppSettingsDocument.Parse(document.ToJson()).GetNode("RateLimiting"));
    }

    [Fact]
    public void Round_trip_of_the_shipped_sample_is_stable()
    {
        // The default settings file of the examples, examples/appsettings/appsettings.json: a file
        // an operator may well start from.
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

    [Theory]
    [InlineData(600)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void An_integer_written_reads_as_a_number_before_the_file_is_reloaded(int value)
    {
        // STUDIO-53: the node SetInt32 creates holds an int, which TryGetValue<double> refused —
        // the validator read the election's 600 s as « not a number » and blocked the save.
        var document = AppSettingsDocument.CreateEmpty();

        document.SetInt32("Llm:TimeoutSeconds", value);

        Assert.Equal(value, document.GetDouble("Llm:TimeoutSeconds"));
        Assert.Equal(value, document.GetInt32("Llm:TimeoutSeconds"));
        AssertReadsAsReloaded(document, "Llm:TimeoutSeconds");
    }

    [Theory]
    [InlineData(600.0, 600)]
    [InlineData(-2.0, -2)]
    [InlineData(0.5, null)]
    [InlineData(3e9, null)]
    public void A_floating_point_value_written_reads_as_an_integer_only_when_it_is_one_that_fits(double value, int? integer)
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.SetDouble("Llm:TimeoutSeconds", value);

        Assert.Equal(value, document.GetDouble("Llm:TimeoutSeconds"));
        Assert.Equal(integer, document.GetInt32("Llm:TimeoutSeconds"));
        AssertReadsAsReloaded(document, "Llm:TimeoutSeconds");
    }

    [Fact]
    public void A_number_reads_as_its_json_text_whatever_created_its_node()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.SetNode("Llm:MaxTokens", JsonValue.Create(4096L));
        document.SetNode("Llm:TimeoutSeconds", JsonValue.Create(3_000_000_000L));
        document.SetNode("Llm:Temperature", JsonValue.Create(0.25m));

        Assert.Equal(4096, document.GetInt32("Llm:MaxTokens"));
        Assert.Null(document.GetInt32("Llm:TimeoutSeconds"));
        Assert.Equal(3e9, document.GetDouble("Llm:TimeoutSeconds"));
        Assert.Equal(0.25, document.GetDouble("Llm:Temperature"));
        Assert.Null(document.GetInt32("Llm:Temperature"));
        foreach (var path in NumericPaths)
            AssertReadsAsReloaded(document, path);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_number_json_has_no_text_for_reads_as_no_number(double value)
    {
        // Read from its JSON text, NaN and the infinities would throw: no file can hold them.
        var document = AppSettingsDocument.CreateEmpty();

        document.SetDouble("Llm:Temperature", value);

        Assert.Null(document.GetDouble("Llm:Temperature"));
        Assert.Null(document.GetInt32("Llm:Temperature"));
    }

    [Fact]
    public void An_integer_beyond_int_in_the_file_reads_as_a_number_and_as_no_integer()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "MaxTokens": 3000000000, "TimeoutSeconds": 600.0 } }""");

        Assert.Null(document.GetInt32("Llm:MaxTokens"));
        Assert.Equal(3e9, document.GetDouble("Llm:MaxTokens"));
        // The configuration binder refuses « 600.0 » for an int; so does the document, as before.
        Assert.Null(document.GetInt32("Llm:TimeoutSeconds"));
        Assert.Equal(600, document.GetDouble("Llm:TimeoutSeconds"));
    }

    /// <summary>A document is worth what it will be worth reread: in memory, a number reads as the saved file will.</summary>
    private static void AssertReadsAsReloaded(AppSettingsDocument document, string path)
    {
        var reloaded = AppSettingsDocument.Parse(document.ToJson());

        Assert.Equal(reloaded.GetInt32(path), document.GetInt32(path));
        Assert.Equal(reloaded.GetDouble(path), document.GetDouble(path));
        Assert.Equal(reloaded.GetString(path), document.GetString(path));
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

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
