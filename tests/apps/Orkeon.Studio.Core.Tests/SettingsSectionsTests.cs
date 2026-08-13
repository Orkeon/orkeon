using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Tests;

/// <summary>The typed views over the sections the UIs put on a form.</summary>
public sealed class SettingsSectionsTests
{
    private static readonly string[] KnownRagProfiles =
        ["fast", "balanced", "quality", "adaptive", "corrective"];

    private static readonly string[] SerializedMounts =
        ["/srv/data:/workspace:ro", "/srv/out:/output:rw"];

    private static readonly string[] RemainingMount = ["/b:/output:rw"];

    [Fact]
    public void Rate_limiting_fields_round_trip()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.RateLimiting.MaxConcurrentRequests = 1;
        document.RateLimiting.GlobalRequestsPerMinute = 60;
        document.RateLimiting.ProviderRequestsPerMinute = 30;
        document.RateLimiting.AgentRequestsPerMinute = 20;
        document.RateLimiting.QueueLimit = 32;

        var reparsed = AppSettingsDocument.Parse(document.ToJson());
        Assert.Equal(1, reparsed.RateLimiting.MaxConcurrentRequests);
        Assert.Equal(60, reparsed.RateLimiting.GlobalRequestsPerMinute);
        Assert.Equal(30, reparsed.RateLimiting.ProviderRequestsPerMinute);
        Assert.Equal(20, reparsed.RateLimiting.AgentRequestsPerMinute);
        Assert.Equal(32, reparsed.RateLimiting.QueueLimit);
    }

    [Fact]
    public void Rag_switches_land_on_the_documented_configuration_keys()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Rag.Profile = "balanced";
        document.Rag.HybridRetrievalEnabled = true;
        document.Rag.CorrectiveWebFallbackEnabled = true;
        document.Rag.WebFallbackEnabled = true;

        Assert.Equal("balanced", document.GetString("Orkeon:Rag:Profile"));
        Assert.True(document.GetBoolean("Orkeon:Rag:Retrieval:Hybrid:Enabled"));
        Assert.True(document.GetBoolean("Orkeon:Rag:Corrective:WebFallback:Enabled"));
        Assert.True(document.GetBoolean("Orkeon:Rag:WebFallback:Enabled"));
        Assert.True(document.Rag.WebFallbackFullyEnabled);
    }

    [Fact]
    public void The_web_fallback_needs_both_halves_of_the_opt_in()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Rag.CorrectiveWebFallbackEnabled = true;

        Assert.False(document.Rag.WebFallbackFullyEnabled);
    }

    [Fact]
    public void The_rag_profile_list_is_the_runtime_one()
    {
        Assert.Equal(
            KnownRagProfiles,
            RagSection.KnownProfiles);
    }

    [Fact]
    public void Log_levels_are_read_and_written_per_category()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Logging": { "LogLevel": { "Default": "Information", "Orkeon": "Debug" } } }""");

        Assert.Equal("Information", document.Logging.DefaultLevel);
        Assert.Equal("Debug", document.Logging.GetLevel("Orkeon"));

        document.Logging.SetLevel("Orkeon", "Warning");
        document.Logging.SetLevel("Microsoft", "Error");

        Assert.Equal(3, document.Logging.Levels.Count);
        Assert.Equal("Warning", document.Logging.Levels["Orkeon"]);
        Assert.Equal("Error", document.Logging.GetLevel("Microsoft"));
    }

    [Fact]
    public void Clearing_a_log_level_removes_the_category()
    {
        var document = AppSettingsDocument.Parse("""{ "Logging": { "LogLevel": { "Default": "Information" } } }""");

        document.Logging.DefaultLevel = null;

        Assert.False(document.Logging.Exists);
    }

    [Fact]
    public void Llm_logging_fields_round_trip()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.LlmLogging.FullEmbeddingLog = false;
        document.LlmLogging.LogStreamingExchanges = true;
        document.LlmLogging.MaxBodyLengthChars = 4096;

        var reparsed = AppSettingsDocument.Parse(document.ToJson());
        Assert.False(reparsed.LlmLogging.FullEmbeddingLog);
        Assert.True(reparsed.LlmLogging.LogStreamingExchanges);
        Assert.Equal(4096, reparsed.LlmLogging.MaxBodyLengthChars);
    }

    [Fact]
    public void Mounts_are_stored_as_the_runtime_string_format()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Mounts.Set(
        [
            new MountDefinition { PhysicalPath = "/srv/data", VirtualPath = "/workspace", Rights = MountRights.ReadOnly },
            new MountDefinition { PhysicalPath = "/srv/out", VirtualPath = "/output", Rights = MountRights.ReadWrite },
        ]);

        Assert.Equal(
            SerializedMounts,
            document.GetStringArray("Orkeon:FileSystem:Mounts"));
        Assert.Equal(2, document.Mounts.Definitions.Count);
    }

    [Fact]
    public void An_unparsable_mount_entry_is_kept_raw_and_skipped_by_the_typed_view()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Mounts.SetRaw(["/srv/data:/workspace:ro", "garbage"]);

        Assert.Equal(2, document.Mounts.RawEntries.Count);
        Assert.Single(document.Mounts.Definitions);
        Assert.Contains("garbage", AppSettingsDocument.Parse(document.ToJson()).Mounts.RawEntries);
    }

    [Fact]
    public void Adding_and_removing_a_mount_keeps_the_other_entries()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Mounts.SetRaw(["/a:/workspace:ro"]);

        document.Mounts.Add(new MountDefinition { PhysicalPath = "/b", VirtualPath = "/output", Rights = MountRights.ReadWrite });
        Assert.Equal(2, document.Mounts.RawEntries.Count);

        document.Mounts.RemoveAt(0);

        Assert.Equal(RemainingMount, document.Mounts.RawEntries);
    }
}
