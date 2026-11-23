using Jint;
using Jint.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Chat.DependencyInjection;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The chat family as a script reaches it — the real tools behind <c>tools.chat*</c> — held to
/// the keys <c>Typings/tools.d.ts</c> declares for them. Every chat call needs an account and a
/// network; the key checks run before any account is resolved, so a family with no account
/// configured is enough for them, and the answer a call without an account gets is the one the
/// typings promise a script: a failure that says what to configure.
/// </summary>
public sealed class ChatToolTypingsTests
{
    [Fact]
    public async Task Every_chat_tool_is_reachable_under_its_declared_camel_case_member()
    {
        await using var services = BuildChatTools();

        var reachable = (await EvaluateAsync(services, """
            Promise.resolve(['chatAccounts', 'chatList', 'chatRead', 'chatMembers', 'chatSend', 'chatSearch', 'chatEdit', 'chatDelete', 'chatReact', 'chatMeetings']
                .every(name => typeof tools[name] === 'function'))
            """)).AsBoolean();

        Assert.True(reachable);
    }

    [Fact]
    public async Task A_required_argument_is_found_by_its_declared_name_only()
    {
        await using var services = BuildChatTools();

        var misnamed = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync(services, "tools.chatSend({ place: 'chat:c1', body: 'hi' })"));
        var named = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync(services, "tools.chatSend({ place: 'chat:c1', text: 'hi' })"));

        Assert.Contains("Required parameter 'text' is missing", misnamed.ToString(), StringComparison.Ordinal);
        Assert.Contains("No chat account is configured", named.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_accounts_of_an_unconfigured_family_are_an_empty_list()
    {
        await using var services = BuildChatTools();

        var count = (await EvaluateAsync(services, "tools.chatAccounts().then(r => r.accounts.length)")).AsNumber();

        Assert.Equal(0d, count);
    }

    private static ServiceProvider BuildChatTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite));
        services.AddOrkeonChatTools(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static Task<JsValue> EvaluateAsync(ServiceProvider services, string script)
    {
        var engine = new JsEngineFactory(builtInTools: services.GetServices<IBaseTool>().ToArray()).Create();
        return Task.Run(() => engine.Evaluate(script).UnwrapIfPromise());
    }
}
