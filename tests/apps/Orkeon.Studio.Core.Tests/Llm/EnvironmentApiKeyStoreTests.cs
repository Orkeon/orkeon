using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// STUDIO-44: a remembered key is found again whatever the parent that started Studio, and a
/// failed persistent write never costs the session its key.
/// </summary>
public sealed class EnvironmentApiKeyStoreTests
{
    [Fact]
    public void A_key_held_only_in_the_user_scope_is_seen_and_copied_into_the_process()
    {
        // Studio relaunched from a terminal opened before the key was remembered:
        // HKCU holds it, the inherited process block does not.
        var environment = new FakeEnvironmentVariables();
        environment.User["ZAI_API_KEY"] = " sk-zai ";
        var store = new EnvironmentApiKeyStore(environment);

        Assert.Equal("sk-zai", store.Peek("ZAI_API_KEY"));
        // Every child process Studio spawns from now on inherits it.
        Assert.Equal("sk-zai", environment.Process["ZAI_API_KEY"]);
    }

    [Fact]
    public void The_process_scope_wins_and_the_user_scope_is_not_written_by_a_read()
    {
        var environment = new FakeEnvironmentVariables();
        environment.Process["ZAI_API_KEY"] = "sk-session";
        environment.User["ZAI_API_KEY"] = "sk-older";
        var store = new EnvironmentApiKeyStore(environment);

        Assert.Equal("sk-session", store.Peek("ZAI_API_KEY"));
        Assert.Empty(environment.Writes);
    }

    [Fact]
    public void A_blank_value_in_either_scope_is_no_key()
    {
        var environment = new FakeEnvironmentVariables();
        environment.Process["ZAI_API_KEY"] = "  ";
        environment.User["ZAI_API_KEY"] = "";
        var store = new EnvironmentApiKeyStore(environment);

        Assert.Null(store.Peek("ZAI_API_KEY"));
    }

    [Fact]
    public async Task Saving_writes_the_process_first_and_the_user_scope_after()
    {
        var environment = new FakeEnvironmentVariables();
        var store = new EnvironmentApiKeyStore(environment);

        await store.SaveAsync("ZAI_API_KEY", "  sk-zai  ");

        Assert.Equal([EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User], environment.Writes);
        Assert.Equal("sk-zai", environment.Process["ZAI_API_KEY"]);
        Assert.Equal("sk-zai", environment.User["ZAI_API_KEY"]);
    }

    [Fact]
    public async Task The_key_is_in_the_process_before_the_persistent_write_completes()
    {
        var environment = new FakeEnvironmentVariables();
        var store = new EnvironmentApiKeyStore(environment);

        var pending = store.SaveAsync("ZAI_API_KEY", "sk-zai");

        // Whatever the user-scope broadcast costs, the session has the key at once.
        Assert.Equal("sk-zai", store.Peek("ZAI_API_KEY"));
        await pending;
    }

    [Fact]
    public async Task A_failing_user_write_faults_the_task_and_keeps_the_process_key()
    {
        var environment = new FakeEnvironmentVariables
        {
            UserWriteFailure = new UnauthorizedAccessException("HKCU\\Environment is read-only"),
        };
        var store = new EnvironmentApiKeyStore(environment);

        var failure = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync("ZAI_API_KEY", "sk-zai"));

        Assert.Contains("read-only", failure.Message, StringComparison.Ordinal);
        Assert.Equal("sk-zai", store.Peek("ZAI_API_KEY"));
        Assert.False(environment.User.ContainsKey("ZAI_API_KEY"));
    }

    [Fact]
    public async Task The_real_environment_round_trips_through_the_process_scope()
    {
        var store = new EnvironmentApiKeyStore();
        var name = $"ORKEON_TEST_KEY_{Guid.NewGuid():N}";
        try
        {
            Assert.Null(store.Peek(name));
            var pending = store.SaveAsync(name, "  sk-value  ");
            Assert.Equal("sk-value", store.Peek(name));
            // The user scope is a documented no-op off Windows; on Windows it really persists.
            await pending;
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
            if (OperatingSystem.IsWindows())
                Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
        }
    }
}
