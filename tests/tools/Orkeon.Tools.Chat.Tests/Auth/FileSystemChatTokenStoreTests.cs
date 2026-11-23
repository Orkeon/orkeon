using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Chat.Auth;

namespace Orkeon.Tools.Chat.Tests.Auth;

/// <summary>OAuth tokens kept as one JSON file per account under a virtual directory.</summary>
public sealed class FileSystemChatTokenStoreTests
{
    private static readonly ChatTokenSet Tokens = new()
    {
        AccessToken = "at-access",
        RefreshToken = "rt-1",
        ExpiresAt = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero),
        Scopes = ["Chat.Read"],
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_write_one_JSON_file_per_key_and_read_it_back()
    {
        var files = new FakeFileSystemService().AddMount("/credentials", FileAccessRights.ReadWrite);
        var store = new FileSystemChatTokenStore(files, "/credentials/chat/");

        await store.WriteAsync("perso-0123456789ab", Tokens, Token);
        var read = await store.ReadAsync("perso-0123456789ab", Token);

        var json = await files.TryReadAllTextAsync("/credentials/chat/perso-0123456789ab.json", Token);
        Assert.NotNull(json);
        Assert.Contains("\"AccessToken\": \"at-access\"", json, StringComparison.Ordinal);
        Assert.NotNull(read);
        Assert.Equal((Tokens.AccessToken, Tokens.RefreshToken, Tokens.ExpiresAt), (read.AccessToken, read.RefreshToken, read.ExpiresAt));
        Assert.Equal(Tokens.Scopes, read.Scopes);
        Assert.True(await files.ExistsAsync("/credentials/chat", Token));
    }

    [Fact]
    public async Task Should_replace_the_tokens_of_a_key()
    {
        var store = Store(out _);
        await store.WriteAsync("perso", Tokens, Token);

        await store.WriteAsync("perso", Tokens with { AccessToken = "at-newer" }, Token);

        Assert.Equal("at-newer", (await store.ReadAsync("perso", Token))?.AccessToken);
    }

    [Fact]
    public async Task Should_delete_every_key_of_an_account_and_of_no_other()
    {
        var store = Store(out var files);
        string[] others = ["work-x-0123456789ab", "work-0123456789abc", "work-0123456789a", "work-0123456789xy", "work2-0123456789ab", "my-work-0123456789ab"];
        foreach (var key in (string[])["work-0123456789ab", "Work-abcdef012345", .. others])
            await store.WriteAsync(key, Tokens, Token);
        files.AddFile("/credentials/chat/work-ba9876543210.json.tmp", "interrupted");

        Assert.True(await store.DeleteAccountAsync("work", keptKey: null, Token));

        Assert.Null(await store.ReadAsync("work-0123456789ab", Token));
        Assert.Null(await store.ReadAsync("Work-abcdef012345", Token));
        Assert.False(await files.ExistsAsync("/credentials/chat/work-ba9876543210.json.tmp", Token));
        foreach (var key in others)
            Assert.NotNull(await store.ReadAsync(key, Token));
        Assert.False(await store.DeleteAccountAsync("work", keptKey: null, Token));
    }

    [Fact]
    public async Task Should_keep_the_key_it_is_told_to_keep()
    {
        var store = Store(out _);
        await store.WriteAsync("work-0123456789ab", Tokens, Token);
        await store.WriteAsync("work-abcdef012345", Tokens, Token);

        Assert.True(await store.DeleteAccountAsync("work", "work-abcdef012345", Token));

        Assert.Null(await store.ReadAsync("work-0123456789ab", Token));
        Assert.NotNull(await store.ReadAsync("work-abcdef012345", Token));
    }

    [Fact]
    public async Task Should_leave_no_temporary_file_behind_a_write()
    {
        var files = new FakeFileSystemService().AddMount("/credentials", FileAccessRights.ReadWrite);
        var store = new FileSystemChatTokenStore(files, "/credentials/chat");

        await store.WriteAsync("work-0123456789ab", Tokens, Token);

        Assert.False(await files.ExistsAsync("/credentials/chat/work-0123456789ab.json.tmp", Token));
        Assert.NotNull(await store.ReadAsync("work-0123456789ab", Token));
    }

    [Fact]
    public async Task Should_answer_null_for_a_missing_blank_or_damaged_file()
    {
        var store = Store(out var files);
        files.AddFile("/credentials/chat/blank.json", "   ");
        files.AddFile("/credentials/chat/damaged.json", "{ \"AccessToken\": ");

        Assert.Null(await store.ReadAsync("missing", Token));
        Assert.Null(await store.ReadAsync("blank", Token));
        Assert.Null(await store.ReadAsync("damaged", Token));
    }

    [Fact]
    public async Task Should_delete_the_file_and_say_whether_there_was_one()
    {
        var store = Store(out var files);
        await store.WriteAsync("perso", Tokens, Token);

        Assert.True(await store.DeleteAsync("perso", Token));
        Assert.False(await store.DeleteAsync("perso", Token));
        Assert.False(await files.ExistsAsync("/credentials/chat/perso.json", Token));
        Assert.Null(await store.ReadAsync("perso", Token));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("with space")]
    [InlineData("   ")]
    public async Task Should_refuse_a_key_that_could_leave_the_directory_or_hide_a_file(string key)
    {
        var store = Store(out _);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ReadAsync(key, Token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.WriteAsync(key, Tokens, Token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DeleteAsync(key, Token));
    }

    [Fact]
    public void Should_require_a_file_system_and_a_directory()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSystemChatTokenStore(null!, "/credentials"));
        Assert.Throws<ArgumentException>(() => new FileSystemChatTokenStore(new FakeFileSystemService(), " "));
    }

    private static FileSystemChatTokenStore Store(out FakeFileSystemService files)
    {
        files = new FakeFileSystemService().AddMount("/credentials", FileAccessRights.ReadWrite);
        return new FileSystemChatTokenStore(files, "/credentials/chat");
    }
}
